using System.Text.Json;
using DevAgents.Models;
using DevAgents.Options;
using DevAgents.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// ---- Configuration -------------------------------------------------------
// appsettings.json  →  config/local.json (your overrides: models, paths, ...)  →  env vars (Ollama__BaseUrl=...)
var root = builder.Environment.ContentRootPath;
var overridePaths = new[]
{
    Path.Combine(root, "config", "local.json"),                              // Docker: /app/config/local.json
    Path.GetFullPath(Path.Combine(root, "..", "..", "config", "local.json")), // dotnet run from repo
    Environment.GetEnvironmentVariable("DEVAGENTS_CONFIG")
};
foreach (var p in overridePaths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct())
    if (File.Exists(p) || p == overridePaths[0])
        builder.Configuration.AddJsonFile(p!, optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

// Default address when nothing is configured (Docker sets ASPNETCORE_URLS=http://+:8080).
if (string.IsNullOrEmpty(builder.Configuration["urls"]) &&
    string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
    builder.WebHost.UseUrls("http://localhost:8080");

builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection("Ollama"));
builder.Services.Configure<PipelineSettings>(builder.Configuration.GetSection("Pipeline"));
builder.Services.Configure<WorkspaceOptions>(builder.Configuration.GetSection("Workspace"));

builder.Services.AddHttpClient<OllamaClient>(c => c.Timeout = Timeout.InfiniteTimeSpan);
builder.Services.AddSingleton<PromptStore>();
builder.Services.AddSingleton<ProjectContextBuilder>();
builder.Services.AddTransient<AgentPipeline>();

var StreamJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// ---- API -----------------------------------------------------------------

app.MapGet("/api/health", async (OllamaClient ollama, CancellationToken ct) =>
{
    try
    {
        var models = await ollama.ListModelsAsync(ct);
        return Results.Ok(new { ok = true, ollama = ollama.BaseUrl, modelCount = models.Count });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { ok = false, ollama = ollama.BaseUrl, error = ex.Message });
    }
});

app.MapGet("/api/models", async (OllamaClient ollama, CancellationToken ct) =>
{
    try { return Results.Ok(await ollama.ListModelsAsync(ct)); }
    catch (Exception ex) { return Results.Problem(ex.Message, statusCode: 502); }
});

app.MapGet("/api/config", (OllamaClient ollama, IOptionsMonitor<PipelineSettings> ps, IOptionsMonitor<WorkspaceOptions> ws) =>
{
    var agents = new[] { "Router", "Translator", "Analyzer", "Coder", "Tester", "Reviewer", "BackTranslator" }
        .Select(a => ollama.Resolve(a, null, a is "BackTranslator" or "Router" ? "Translator" : null));
    return Results.Ok(new { ollama = ollama.BaseUrl, agents, pipeline = ps.CurrentValue, workspace = ws.CurrentValue });
});

app.MapGet("/api/projects", (IOptionsMonitor<WorkspaceOptions> ws) =>
{
    var roots = ws.CurrentValue.AllowedRoots;
    var projects = roots.Where(Directory.Exists)
        .SelectMany(r => Directory.GetDirectories(r).Select(d => new { name = Path.GetFileName(d), path = d }))
        .OrderBy(p => p.name).ToList();
    return Results.Ok(new { roots, projects });
});

app.MapPost("/api/context/preview", (ContextPreviewRequest req, ProjectContextBuilder ctxBuilder) =>
{
    try
    {
        var ctx = ctxBuilder.Build(req.ProjectPath, req.Files, req.Question ?? "");
        return Results.Ok(new { overview = ctx.Overview, included = ctx.Included, totalFiles = ctx.TotalFiles, chars = ctx.Chars, warning = ctx.Warning });
    }
    catch (Exception ex) { return Results.Problem(ex.Message, statusCode: 400); }
});

// Single JSON response (use this from scripts / other tools).
app.MapPost("/api/chat", async (ChatRequest req, AgentPipeline pipeline, CancellationToken ct) =>
{
    try { return Results.Ok(await pipeline.RunAsync(req, _ => Task.CompletedTask, ct)); }
    catch (OperationCanceledException) { return Results.StatusCode(499); }
    catch (Exception ex) { return Results.Problem(ex.Message, statusCode: 500); }
});

// Streaming NDJSON: one JSON event per line (stage_start | token | stage_end | final | error).
// Used by the web chat and the VS Code extension.
app.MapPost("/api/chat/stream", async (ChatRequest req, AgentPipeline pipeline, HttpContext http) =>
{
    http.Response.ContentType = "application/x-ndjson";
    http.Response.Headers.CacheControl = "no-cache";
    http.Response.Headers["X-Accel-Buffering"] = "no";
    var ct = http.RequestAborted;

    async Task Emit(PipelineEvent e)
    {
        await http.Response.WriteAsync(JsonSerializer.Serialize(e, StreamJson) + "\n", ct);
        await http.Response.Body.FlushAsync(ct);
    }

    try
    {
        var result = await pipeline.RunAsync(req, Emit, ct);
        await Emit(new PipelineEvent("final", "done", Data: result));
    }
    catch (OperationCanceledException) { /* client disconnected */ }
    catch (Exception ex)
    {
        try { await Emit(new PipelineEvent("error", "", ex.Message)); } catch { }
    }
});
app.MapPost("/v1/chat/completions", async (HttpContext http, AgentPipeline pipeline) =>
{
    http.Response.ContentType = "text/event-stream";
    http.Response.Headers.CacheControl = "no-cache";
    http.Response.Headers["X-Accel-Buffering"] = "no";
    var ct = http.RequestAborted;

    // 1. Continue'dan gelen JSON verisini oku
    using var reader = new StreamReader(http.Request.Body);
    var bodyText = await reader.ReadToEndAsync(ct);
    
    using var doc = JsonDocument.Parse(bodyText);
    var root = doc.RootElement;

    string userMessage = "";
    if (root.TryGetProperty("messages", out var messagesEl) && messagesEl.ValueKind == JsonValueKind.Array)
    {
        var lastMsg = messagesEl.EnumerateArray().LastOrDefault();
        if (lastMsg.ValueKind != JsonValueKind.Undefined && lastMsg.TryGetProperty("content", out var contentEl))
        {
            userMessage = contentEl.GetString() ?? "";
        }
    }

    if (string.IsNullOrWhiteSpace(userMessage))
    {
        http.Response.StatusCode = 400;
        await http.Response.WriteAsync("Prompt/Message bulunamadı.", ct);
        return;
    }

    bool isStream = !root.TryGetProperty("stream", out var streamEl) || streamEl.GetBoolean();

    // ChatRequest record tanımınıza uygun nesne oluşturuluyor
    var req = new ChatRequest(Question: userMessage);
    var responseId = "chatcmpl-" + Guid.NewGuid().ToString("N");

    async Task SendOpenAiChunkAsync(string? textDelta, string? finishReason = null)
    {
        var chunk = new
        {
            id = responseId,
            @object = "chat.completion.chunk",
            created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            model = "csharp-multi-agent",
            choices = new[]
            {
                new
                {
                    index = 0,
                    delta = textDelta != null ? new { content = textDelta } : new object(),
                    finish_reason = finishReason
                }
            }
        };

        await http.Response.WriteAsync($"data: {JsonSerializer.Serialize(chunk)}\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
    }

    try
    {
        if (isStream)
        {
            var result = await pipeline.RunAsync(req, async evt =>
            {
                // Streaming esnasında token geldikçe anlık ekrana basıyoruz
                if (evt.Type == "token" && !string.IsNullOrEmpty(evt.Text))
                {
                    await SendOpenAiChunkAsync(evt.Text);
                }
                else if (evt.Type == "stage_start")
                {
                    await SendOpenAiChunkAsync($"\n\n> 🤖 **{evt.Text ?? evt.Stage}** çalışıyor...\n\n");
                }
            }, ct);

            // Eğer event akışı esnasında nihai çıktı verilmediyse FinalMarkdown basılıyor
            if (result != null && !string.IsNullOrEmpty(result.FinalMarkdown))
            {
                 await SendOpenAiChunkAsync(result.FinalMarkdown);
            }

            await SendOpenAiChunkAsync(null, "stop");
            await http.Response.WriteAsync("data: [DONE]\n\n", ct);
            await http.Response.Body.FlushAsync(ct);
        }
        else
        {
            var result = await pipeline.RunAsync(req, _ => Task.CompletedTask, ct);
            
            var nonStreamResponse = new
            {
                id = responseId,
                @object = "chat.completion",
                created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                model = "csharp-multi-agent",
                choices = new[]
                {
                    new
                    {
                        index = 0,
                        message = new { role = "assistant", content = result?.FinalMarkdown ?? "" },
                        finish_reason = "stop"
                    }
                }
            };

            http.Response.ContentType = "application/json";
            await http.Response.WriteAsync(JsonSerializer.Serialize(nonStreamResponse), ct);
        }
    }
    catch (OperationCanceledException) { /* İstemci bağlantıyı kesti */ }
    catch (Exception ex)
    {
        if (isStream)
        {
            await SendOpenAiChunkAsync($"\n\n[Hata]: {ex.Message}", "stop");
            await http.Response.WriteAsync("data: [DONE]\n\n", ct);
        }
        else
        {
            http.Response.StatusCode = 500;
            await http.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }), ct);
        }
    }
});

app.Run();
