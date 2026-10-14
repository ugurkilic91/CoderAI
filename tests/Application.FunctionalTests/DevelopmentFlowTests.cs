using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Shouldly;

namespace DevAgents.Application.FunctionalTests;

/// <summary>
/// "Projeye uygula" akışı HTTP üzerinden: mimari seçimi, dosyaların projeye yazılması, derleme/test sonucu,
/// değişikliklerin geri alınması ve yeni proje oluşturma.
/// </summary>
public class DevelopmentFlowTests
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

    // Her test kendi proje klasöründe çalışır: aynı dosyaları tekrar yazmak "değişiklik yok" demektir.
    private string CreateProject(string name)
    {
        var root = Path.Combine(_factory.WorkspaceRoot, name);
        Directory.CreateDirectory(Path.Combine(root, "Services"));
        File.WriteAllText(Path.Combine(root, name + ".csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(Path.Combine(root, "Services", "OrderService.cs"), "public class OrderService { }");
        return root;
    }

    [Test]
    public async Task ShouldListTheCleanArchitectureProfile()
    {
        var architectures = JsonNode.Parse(await _client.GetStringAsync("/api/architectures"))!.AsArray();

        var clean = architectures.Single(a => (string)a!["id"]! == "clean-architecture")!;
        ((string)clean["name"]!).ShouldBe("Clean Architecture (CQRS + MediatR)");
        ((bool)clean["canCreateProjects"]!).ShouldBeTrue();
        clean["testProjects"]!.AsArray().Select(p => (string)p!).ShouldBe(["tests/Domain.UnitTests", "tests/Application.UnitTests"]);
    }

    [Test]
    public async Task ShouldWriteTheCodeIntoTheProjectAndRevertIt()
    {
        var shop = CreateProject("RevertShop");

        using var response = await _client.PostAsJsonAsync("/api/chat", new
        {
            question = "Sipariş iptali ekle",
            projectPath = "RevertShop",
            architecture = "clean-architecture",
            applyChanges = true
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var development = result["development"]!;
        ((string)development["projectPath"]!).ShouldBe(shop);
        ((bool)development["build"]!["succeeded"]!).ShouldBeTrue();
        ((int)development["tests"]!["passed"]!).ShouldBe(4);
        development["changes"]!.AsArray().Select(c => $"{c!["path"]}:{c["kind"]}").ShouldBe(["src/Foo.cs:created", "tests/FooTests.cs:created"]);
        File.ReadAllText(Path.Combine(shop, "src", "Foo.cs")).ShouldBe("class Foo {}\n");
        ((string)result["finalMarkdown"]!).ShouldStartWith("## Geliştirme özeti");

        using var revert = await _client.PostAsJsonAsync("/api/changes/revert", new
        {
            projectPath = "RevertShop",
            snapshot = (string)development["snapshot"]!
        });

        revert.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonNode.Parse(await revert.Content.ReadAsStringAsync())!.AsArray().Count.ShouldBe(2);
        File.Exists(Path.Combine(shop, "src", "Foo.cs")).ShouldBeFalse();
        File.Exists(Path.Combine(shop, "Services", "OrderService.cs")).ShouldBeTrue();
    }

    [Test]
    public async Task ShouldStreamTheDevelopmentStages()
    {
        CreateProject("StreamShop");
        using var response = await _client.PostAsJsonAsync("/api/chat/stream", new
        {
            question = "Sipariş iptali ekle",
            projectPath = "StreamShop",
            architecture = "clean-architecture",
            applyChanges = true
        });
        var events = (await response.Content.ReadAsStringAsync())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonNode.Parse(line)!).ToList();

        events.Where(e => (string)e["type"]! == "stage_start").Select(e => (string)e["stage"]!)
            .ShouldBe(["translate_in", "route", "context", "analyze", "code", "syntax", "write", "build", "test", "syntax", "write", "build", "test_run", "review", "translate_out"]);
        var testRun = events.Single(e => (string)e["type"]! == "stage_end" && (string)e["stage"]! == "test_run");
        ((string)testRun["text"]!).ShouldBe("4/4 başarılı");
        ((string)events.Last()["type"]!).ShouldBe("final");
    }

    [Test]
    public async Task ShouldCreateANewProjectFromTheTemplate()
    {
        using var response = await _client.PostAsJsonAsync("/api/chat", new
        {
            question = "Hobilerimi kaydeden kodu geliştir",
            architecture = "clean-architecture",
            applyChanges = true,
            newProjectName = "HobbyTracker"
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var development = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["development"]!;
        ((bool)development["projectCreated"]!).ShouldBeTrue();
        ((string)development["projectPath"]!).ShouldBe(Path.Combine(_factory.WorkspaceRoot, "HobbyTracker"));
        File.Exists(Path.Combine(_factory.WorkspaceRoot, "HobbyTracker", "src", "Foo.cs")).ShouldBeTrue();
    }

    [Test]
    public async Task ShouldRejectApplyingWithoutAProject()
    {
        using var response = await _client.PostAsJsonAsync("/api/chat", new { question = "x", applyChanges = true });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        ((string)JsonNode.Parse(await response.Content.ReadAsStringAsync())!["detail"]!)
            .ShouldBe("Kodun yazılacağı proje klasörünü seçin ya da yeni proje adı verin.");
    }

    [Test]
    public async Task ShouldReportTheSandboxInTheConfiguration()
    {
        var configuration = JsonNode.Parse(await _client.GetStringAsync("/api/config"))!;

        ((bool)configuration["sandbox"]!["available"]!).ShouldBeTrue();
        ((int)configuration["development"]!["maxBuildFixAttempts"]!).ShouldBe(3);
    }
}
