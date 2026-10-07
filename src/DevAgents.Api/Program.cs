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

app.Run();
