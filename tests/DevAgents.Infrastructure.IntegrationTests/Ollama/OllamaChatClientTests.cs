using System.Net;
using System.Text;
using System.Text.Json;
using DevAgents.Application.Common.Exceptions;
using DevAgents.Application.Common.Models;
using DevAgents.Infrastructure.Ollama;

namespace DevAgents.Infrastructure.IntegrationTests.Ollama;

public class OllamaChatClientTests
{
    private static readonly AgentModel Coder = new("Coder", "qwen2.5-coder:14b", 0.15, 16384, 8192);

    private static OllamaChatClient CreateClient(StubHttpHandler handler, OllamaOptions? options = null) =>
        new(new HttpClient(handler), new StaticOptionsMonitor<OllamaOptions>(options ?? new OllamaOptions { BaseUrl = "http://ollama.test/" }));

    private static StubHttpHandler Responding(HttpResponseMessage response) => new((_, _) => Task.FromResult(response));

    private static HttpResponseMessage Ndjson(params object[] lines) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(string.Join("\n", lines.Select(line => JsonSerializer.Serialize(line))) + "\n", Encoding.UTF8, "application/x-ndjson")
    };

    [Test]
    public async Task ChatAsync_StreamsTokensAndReturnsTheWholeAnswer()
    {
        var handler = Responding(Ndjson(
            new { message = new { role = "assistant", content = "Hello" }, done = false },
            new { message = new { role = "assistant", content = ", world" }, done = false },
            new { done = true }));
        var tokens = new List<string>();

        var answer = await CreateClient(handler).ChatAsync(Coder, "system", "user", token =>
        {
            tokens.Add(token);
            return Task.CompletedTask;
        }, CancellationToken.None);

        Assert.That(answer, Is.EqualTo("Hello, world"));
        Assert.That(tokens, Is.EqualTo(new[] { "Hello", ", world" }));
        var (request, body) = handler.Requests.Single();
        Assert.That(request.RequestUri, Is.EqualTo(new Uri("http://ollama.test/api/chat")));
        Assert.That(body, Is.EqualTo(
            """{"model":"qwen2.5-coder:14b","stream":true,"keep_alive":"10m","messages":[{"role":"system","content":"system"},{"role":"user","content":"user"}],"options":{"temperature":0.15,"num_ctx":16384,"num_predict":8192}}"""));
    }

    [Test]
    public async Task ChatAsync_ModelNotInstalled_ThrowsWithAPullHint()
    {
        var handler = Responding(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"error":"model not found"}""") });

        var exception = await Assert.ThrowsAsync<LanguageModelException>(() => CreateClient(handler).ChatAsync(Coder, "s", "u", null, CancellationToken.None));

        Assert.That(exception!.Message, Is.EqualTo(
            """Ollama hatası 404 (Coder / qwen2.5-coder:14b): {"error":"model not found"} Model yüklü olmayabilir: 'ollama pull qwen2.5-coder:14b'"""));
    }

    [Test]
    public async Task ChatAsync_ErrorWhileStreaming_Throws()
    {
        var handler = Responding(Ndjson(new { message = new { role = "assistant", content = "par" } }, new { error = "out of memory" }));

        var exception = await Assert.ThrowsAsync<LanguageModelException>(() => CreateClient(handler).ChatAsync(Coder, "s", "u", null, CancellationToken.None));

        Assert.That(exception!.Message, Is.EqualTo("Ollama (qwen2.5-coder:14b): out of memory"));
    }

    [Test]
    public async Task ChatAsync_ServerUnreachable_Throws()
    {
        var handler = new StubHttpHandler((_, _) => throw new HttpRequestException("Connection refused"));

        var exception = await Assert.ThrowsAsync<LanguageModelException>(() => CreateClient(handler).ChatAsync(Coder, "s", "u", null, CancellationToken.None));

        Assert.That(exception!.Message, Is.EqualTo("Ollama'ya bağlanılamadı (http://ollama.test): Connection refused. Ollama çalışıyor mu?"));
    }

    [Test]
    public async Task ChatAsync_NoAnswerWithinTheTimeout_ThrowsTimeout()
    {
        var handler = new StubHttpHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage();
        });
        var client = CreateClient(handler, new OllamaOptions { BaseUrl = "http://ollama.test", TimeoutMinutes = 0 });

        var exception = await Assert.ThrowsAsync<LanguageModelTimeoutException>(() => client.ChatAsync(Coder, "s", "u", null, CancellationToken.None));

        Assert.That(exception!.Message, Does.StartWith("Coder (qwen2.5-coder:14b) 0 dakika içinde yanıt vermedi."));
    }

    [Test]
    public async Task ChatAsync_CallerCancels_CancellationIsNotReportedAsTimeout()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.CatchAsync<OperationCanceledException>(
            () => CreateClient(Responding(Ndjson())).ChatAsync(Coder, "s", "u", null, cancellation.Token));
    }

    [Test]
    public async Task ListModelsAsync_ReturnsTheModelNamesSorted()
    {
        var handler = Responding(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"models":[{"name":"qwen2.5-coder:7b"},{"name":"llama3:8b"}]}""")
        });

        var models = await CreateClient(handler).ListModelsAsync(CancellationToken.None);

        Assert.That(models, Is.EqualTo(new[] { "llama3:8b", "qwen2.5-coder:7b" }));
        Assert.That(handler.Requests.Single().Request.RequestUri, Is.EqualTo(new Uri("http://ollama.test/api/tags")));
    }

    [Test]
    public async Task ListModelsAsync_ServerError_Throws()
    {
        var handler = Responding(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<LanguageModelException>(() => CreateClient(handler).ListModelsAsync(CancellationToken.None));
    }
}
