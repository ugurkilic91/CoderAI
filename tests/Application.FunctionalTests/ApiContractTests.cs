using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Shouldly;

namespace DevAgents.Application.FunctionalTests;

/// <summary>
/// Web arayüzü, VS Code eklentisi ve Continue'nun dayandığı HTTP sözleşmesi:
/// uç noktalar, JSON alan adları, NDJSON olayları ve hata biçimleri.
/// </summary>
public class ApiContractTests
{
    private DevAgentsApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public void StartApplication()
    {
        _factory = new DevAgentsApplicationFactory();
        _client = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public void StopApplication()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<List<JsonNode>> StreamAsync(object body)
    {
        using var response = await _client.PostAsJsonAsync("/api/chat/stream", body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/x-ndjson");
        var text = await response.Content.ReadAsStringAsync();
        return [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonNode.Parse(line)!)];
    }

    [Test]
    public async Task ShouldStreamTheAnswerFlowAsNdjsonEvents()
    {
        var events = await StreamAsync(new { question = "ANSWERONLY record nedir?" });

        events.Where(e => (string)e["type"]! == "stage_start").Select(e => (string)e["stage"]!)
            .ShouldBe(["translate_in", "route", "answer", "translate_out"]);
        var plan = events.Single(e => (string)e["type"]! == "plan");
        ((string)plan["text"]!).ShouldBe("Genel soru.");
        ((string)plan["data"]!["intent"]!).ShouldBe("question");

        var final = events.Last();
        ((string)final["type"]!).ShouldBe("final");
        ((string)final["stage"]!).ShouldBe("done");
        var result = final["data"]!;
        ((string)result["englishRequest"]!).ShouldBe("EN: ANSWERONLY record nedir?");
        ((string)result["finalMarkdown"]!).ShouldStartWith("TR: ");
        result["models"]!.AsObject().Select(model => model.Key).ShouldBe(["Translator", "Router", "Analyzer", "BackTranslator"]);
        ((string)result["context"]!["warning"]!).ShouldBe("Proje yolu veya dosya verilmedi; agent'lar genel bilgiyle çalışacak.");
    }

    [Test]
    public async Task ShouldStreamAValidationErrorAsAnErrorEvent()
    {
        var events = await StreamAsync(new { question = "  " });

        events.Count.ShouldBe(1);
        ((string)events[0]["type"]!).ShouldBe("error");
        ((string)events[0]["text"]!).ShouldBe("Soru boş olamaz.");
    }

    [Test]
    public async Task ShouldRunTheFullDevelopmentFlowOnAProject()
    {
        using var response = await _client.PostAsJsonAsync("/api/chat", new { question = "Sipariş iptali ekle", projectPath = "Shop" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        ((bool)result["approved"]!).ShouldBeTrue();
        result["files"]!.AsArray().Select(file => $"{file!["path"]}:{file["kind"]}").ShouldBe(["src/Foo.cs:code", "tests/FooTests.cs:test"]);
        ((int)result["context"]!["totalFiles"]!).ShouldBe(3);
        result["context"]!["includedFiles"]!.AsArray().Select(file => (string)file!).ShouldContain("Shop.csproj");
        ((string)result["plan"]!["intent"]!).ShouldBe("feature");
    }

    [Test]
    public async Task ShouldReturnBadRequestWithTheMessageForAnEmptyQuestion()
    {
        using var response = await _client.PostAsJsonAsync("/api/chat", new { question = "" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        ((string)problem["detail"]!).ShouldBe("Soru boş olamaz.");
        ((string)problem["errors"]!["Question"]![0]!).ShouldBe("Soru boş olamaz.");
    }

    [Test]
    public async Task ShouldRejectAProjectOutsideTheWorkspace()
    {
        using var response = await _client.PostAsJsonAsync("/api/chat", new { question = "x", projectPath = "/etc" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        ((string)problem["detail"]!).ShouldStartWith("Yol izin verilen kök dizinlerin dışında");
    }

    [Test]
    public async Task ShouldReturnBadGatewayWhenTheModelServerFails()
    {
        using var response = await _client.PostAsJsonAsync("/api/chat", new { question = "FAIL" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        ((string)problem["detail"]!).ShouldStartWith("Ollama'ya bağlanılamadı");
    }

    [Test]
    public async Task ShouldPreviewTheContextOfAProject()
    {
        using var response = await _client.PostAsJsonAsync("/api/context/preview", new { projectPath = "Shop", question = "OrderService" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var preview = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        preview.Select(property => property.Key).ShouldBe(["overview", "included", "totalFiles", "chars", "warning"]);
        ((string)preview["overview"]!).ShouldStartWith("Project: Shop (3 indexed files)");
        preview["included"]!.AsArray().Select(file => (string)file!).ShouldContain("Services/OrderService.cs");
    }

    [Test]
    public async Task ShouldReportHealthModelsConfigurationAndProjects()
    {
        var health = JsonNode.Parse(await _client.GetStringAsync("/api/health"))!;
        health.ToJsonString().ShouldBe("""{"ok":true,"ollama":"http://ollama.test","modelCount":2}""");

        (await _client.GetStringAsync("/api/models")).ShouldBe("""["model-a","model-b"]""");

        var configuration = JsonNode.Parse(await _client.GetStringAsync("/api/config"))!;
        configuration["agents"]!.AsArray().Select(agent => (string)agent!["name"]!)
            .ShouldBe(["Router", "Translator", "Analyzer", "Coder", "Tester", "Reviewer", "BackTranslator"]);
        configuration["pipeline"]!["maxReviewIterations"].ShouldNotBeNull();
        ((string)configuration["workspace"]!["allowedRoots"]![0]!).ShouldBe(_factory.WorkspaceRoot);

        var projects = JsonNode.Parse(await _client.GetStringAsync("/api/projects"))!;
        ((string)projects["projects"]![0]!["name"]!).ShouldBe("Shop");
    }

    [Test]
    public async Task ShouldStreamAnOpenAiCompatibleCompletion()
    {
        using var response = await _client.PostAsJsonAsync("/v1/chat/completions", new
        {
            model = "csharp-multi-agent",
            messages = new[] { new { role = "user", content = "ANSWERONLY nedir?" } }
        });

        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/event-stream");
        var data = (await response.Content.ReadAsStringAsync())
            .Split('\n').Where(line => line.StartsWith("data: ", StringComparison.Ordinal)).Select(line => line[6..]).ToList();
        data[^1].ShouldBe("[DONE]");
        var chunks = data.SkipLast(1).Select(chunk => JsonNode.Parse(chunk)!).ToList();
        chunks.ShouldAllBe(chunk => (string)chunk["object"]! == "chat.completion.chunk" && (string)chunk["model"]! == "csharp-multi-agent");
        var last = chunks[^1]["choices"]![0]!;
        ((string)last["finish_reason"]!).ShouldBe("stop");
        last["delta"]!.ToJsonString().ShouldBe("{}");
    }

    [Test]
    public async Task ShouldReturnASingleOpenAiCompatibleCompletion()
    {
        using var response = await _client.PostAsJsonAsync("/v1/chat/completions", new
        {
            messages = new object[] { new { role = "user", content = new[] { new { type = "text", text = "ANSWERONLY nedir?" } } } },
            stream = false
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var completion = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        ((string)completion["object"]!).ShouldBe("chat.completion");
        ((string)completion["choices"]![0]!["message"]!["content"]!).ShouldStartWith("TR: ");
    }

    [Test]
    public async Task ShouldRejectAnOpenAiRequestWithoutMessages()
    {
        using var response = await _client.PostAsync("/v1/chat/completions", new StringContent("{}", Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Prompt/Message bulunamadı.");
    }

    [Test]
    public async Task ShouldServeTheWebUiAndTheApiDocument()
    {
        var page = await _client.GetStringAsync("/");
        page.ShouldContain("<html");

        var document = await _client.GetFromJsonAsync<JsonDocument>("/openapi/v1.json");
        document!.RootElement.GetProperty("paths").TryGetProperty("/api/chat/stream", out _).ShouldBeTrue();
    }
}
