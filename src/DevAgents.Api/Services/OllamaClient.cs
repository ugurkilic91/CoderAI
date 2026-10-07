using System.Net;
using System.Text;
using System.Text.Json;
using DevAgents.Options;
using Microsoft.Extensions.Options;

namespace DevAgents.Services;

public sealed record ResolvedAgent(string Name, string Model, double Temperature, int NumCtx, int NumPredict);

public sealed class OllamaClient(HttpClient http, IOptionsMonitor<OllamaOptions> opts)
{
    /// <summary>Resolves model + sampling settings: request override → Agents[name] → Agents[fallback] → Defaults.</summary>
    public ResolvedAgent Resolve(string agent, IDictionary<string, string>? overrides = null, string? fallbackAgent = null)
    {
        var o = opts.CurrentValue;
        o.Agents.TryGetValue(agent, out var a);
        if (a == null && fallbackAgent != null) o.Agents.TryGetValue(fallbackAgent, out a);
        var d = o.Defaults;

        string? overridden = null;
        if (overrides != null && overrides.TryGetValue(agent, out var m) && !string.IsNullOrWhiteSpace(m))
            overridden = m;

        return new ResolvedAgent(
            agent,
            overridden ?? a?.Model ?? d.Model ?? "qwen2.5-coder:7b",
            a?.Temperature ?? d.Temperature ?? 0.2,
            a?.NumCtx ?? d.NumCtx ?? 8192,
            a?.NumPredict ?? d.NumPredict ?? 4096);
    }

    public string BaseUrl => opts.CurrentValue.BaseUrl.TrimEnd('/');

    public async Task<List<string>> ListModelsAsync(CancellationToken ct)
    {
        try
        {
            using var resp = await http.GetAsync(BaseUrl + "/api/tags", ct);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            return doc.RootElement.GetProperty("models").EnumerateArray()
                .Select(m => m.GetProperty("name").GetString()!)
                .OrderBy(n => n).ToList();
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Ollama'ya bağlanılamadı ({BaseUrl}): {ex.Message}");
        }
    }

    /// <summary>Streams a chat completion from Ollama, forwarding tokens to <paramref name="onToken"/>; returns the full text.</summary>
    public async Task<string> ChatAsync(ResolvedAgent agent, string system, string user,
        Func<string, Task>? onToken, CancellationToken ct)
    {
        var o = opts.CurrentValue;
        var payload = new
        {
            model = agent.Model,
            stream = true,
            keep_alive = o.KeepAlive,
            messages = new[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user }
            },
            options = new { temperature = agent.Temperature, num_ctx = agent.NumCtx, num_predict = agent.NumPredict }
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(o.TimeoutMinutes));

        using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/api/chat")
        {
            // StringContent (not JsonContent) so the request carries Content-Length instead of chunked encoding
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        try
        {
            HttpResponseMessage resp;
            try
            {
                resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException(
                    $"Ollama'ya bağlanılamadı ({BaseUrl}): {ex.Message}. Ollama çalışıyor mu?");
            }

            using (resp)
            {
                if (!resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync(cts.Token);
                    var hint = resp.StatusCode == HttpStatusCode.NotFound
                        ? $" Model yüklü olmayabilir: 'ollama pull {agent.Model}'"
                        : "";
                    throw new InvalidOperationException(
                        $"Ollama hatası {(int)resp.StatusCode} ({agent.Name} / {agent.Model}): {body}{hint}");
                }

                await using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
                using var reader = new StreamReader(stream);
                var sb = new StringBuilder();
                string? line;
                while ((line = await reader.ReadLineAsync(cts.Token)) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("error", out var err))
                        throw new InvalidOperationException($"Ollama ({agent.Model}): {err.GetString()}");

                    if (root.TryGetProperty("message", out var msg) &&
                        msg.TryGetProperty("content", out var c))
                    {
                        var t = c.GetString();
                        if (!string.IsNullOrEmpty(t))
                        {
                            sb.Append(t);
                            if (onToken != null) await onToken(t);
                        }
                    }
                }
                return MarkdownUtil.StripThinking(sb.ToString());
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"{agent.Name} ({agent.Model}) {o.TimeoutMinutes} dakika içinde yanıt vermedi. Ollama:TimeoutMinutes değerini artırabilirsiniz.");
        }
    }
}
