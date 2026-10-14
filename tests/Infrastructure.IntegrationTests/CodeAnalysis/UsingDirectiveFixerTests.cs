using DevAgents.Domain.ValueObjects;
using DevAgents.Infrastructure.CodeAnalysis;
using DevAgents.Infrastructure.Storage;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Shouldly;

namespace DevAgents.Infrastructure.IntegrationTests.CodeAnalysis;

/// <summary>
/// Eksik using'lerin model kullanmadan eklenmesi: gerçek dosyalar, gerçek .csproj başvuruları ve gerçek kütüphaneler
/// (NUnit, Shouldly, ASP.NET Core). Hata konumları derleyicinin vereceği satır/sütun biçimindedir.
/// </summary>
public class UsingDirectiveFixerTests
{
    private string _root = null!;
    private string _data = null!;
    private UsingDirectiveFixer _fixer = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Directory.CreateTempSubdirectory("devagents-usings-").FullName;
        _data = Directory.CreateTempSubdirectory("devagents-usings-data-").FullName;
        _fixer = new UsingDirectiveFixer(new StaticOptionsMonitor<StorageOptions>(new StorageOptions { DataDirectory = _data }),
            NullLogger<UsingDirectiveFixer>.Instance);

        Write("src/Domain/Domain.csproj", Project());
        Write("src/Domain/Entities/Hobby.cs", "namespace Shop.Domain.Entities;\n\npublic class Hobby { public string? Name { get; set; } }\n");
        Write("src/Application/Application.csproj", Project(@"..\Domain\Domain.csproj"));
        Write("src/Application/Common/Interfaces/IHobbyStore.cs", "namespace Shop.Application.Common.Interfaces;\n\npublic interface IHobbyStore { }\n");
        // Uygulamanın başvurmadığı bir proje: buradaki tipler Application'a önerilmemeli.
        Write("tools/Seeder/Seeder.csproj", Project());
        Write("tools/Seeder/SeedData.cs", "namespace Shop.Seeder;\n\npublic class SeedData { }\n");
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_root, recursive: true);
        Directory.Delete(_data, recursive: true);
    }

    [Test]
    public async Task ShouldAddUsingsForTypesOfTheProjectAndItsReferences()
    {
        const string path = "src/Application/Hobbies/CreateHobby.cs";
        const string code = "namespace Shop.Application.Hobbies;\n\npublic class CreateHobby(IHobbyStore store)\n{\n    public Hobby Create() => new();\n}\n";
        Write(path, code);

        var fixes = await _fixer.FixAsync(_root,
            [Error(path, code, "CS0246", "IHobbyStore"), Error(path, code, "CS0246", "public Hobby", "Hobby")], CancellationToken.None);

        var fix = fixes.ShouldHaveSingleItem();
        fix.Path.ShouldBe(path);
        fix.AddedNamespaces.ShouldBe(["Shop.Application.Common.Interfaces", "Shop.Domain.Entities"]);
        fix.Content.ShouldBe("using Shop.Application.Common.Interfaces;\nusing Shop.Domain.Entities;\n\n" + code);
    }

    [Test]
    public async Task ShouldLeaveUnknownAmbiguousAndAlreadyVisibleNamesToTheFixerAgent()
    {
        Write("src/Domain/Common/Status.cs", "namespace Shop.Domain.Common;\n\npublic enum Status { Active }\n");
        Write("src/Domain/Enums/Status.cs", "namespace Shop.Domain.Enums;\n\npublic enum Status { Active }\n");
        Write("src/Application/GlobalUsings.cs", "global using Shop.Domain.Entities;\n");
        const string path = "src/Application/Hobbies/Report.cs";
        const string code = "namespace Shop.Application.Hobbies;\n\npublic class Report(SeedData seed, Status status, Hobby hobby, TestBase test) { }\n";
        Write(path, code);

        var fixes = await _fixer.FixAsync(_root,
        [
            Error(path, code, "CS0246", "SeedData"), // başvurulmayan projede
            Error(path, code, "CS0246", "Status"),   // iki namespace'te var
            Error(path, code, "CS0246", "Hobby"),    // global using ile zaten görünür: sorun using değil
            Error(path, code, "CS0246", "TestBase")  // hiç yok
        ], CancellationToken.None);

        fixes.ShouldBeEmpty();
    }

    [Test]
    public async Task ShouldFindPackageAndFrameworkTypesThroughTheRestoreAssets()
    {
        Assume.That(FrameworkExists("Microsoft.AspNetCore.App"), "ASP.NET Core paylaşılan çerçevesi kurulu değil.");
        // Docker sandbox'taki restore paket klasörünü konteyner yoluyla yazar; paketler bu makinede DataDirectory/sandbox-home altında.
        var packages = Path.Combine(_data, "sandbox-home", ".nuget", "packages");
        CopyAssembly(typeof(Should).Assembly.Location, Path.Combine(packages, "shouldly", "4.3.0", "lib", "net8.0"));
        CopyAssembly(typeof(TestAttribute).Assembly.Location, Path.Combine(packages, "nunit", "5.0.0", "lib", "net8.0"));
        Write("src/Web/Web.csproj", Project(@"..\Application\Application.csproj"));
        // ArtifactsPath kullanan çözüm düzeni: artifacts/obj/<Proje>/project.assets.json
        Write("artifacts/obj/Web/project.assets.json", """
            {
              "targets": {
                "net10.0": {
                  "NUnit/5.0.0": { "type": "package", "compile": { "lib/net8.0/nunit.framework.dll": {} } },
                  "Shouldly/4.3.0": { "type": "package", "compile": { "lib/net8.0/Shouldly.dll": {} } },
                  "Application/1.0.0": { "type": "project" }
                }
              },
              "libraries": {
                "NUnit/5.0.0": { "type": "package", "path": "nunit/5.0.0" },
                "Shouldly/4.3.0": { "type": "package", "path": "shouldly/4.3.0" }
              },
              "packageFolders": { "/home/sandbox/.nuget/packages/": {} },
              "project": { "frameworks": { "net10.0": { "frameworkReferences": { "Microsoft.AspNetCore.App": {}, "Microsoft.NETCore.App": {} } } } }
            }
            """);
        Write("artifacts/obj/Web/debug/Web.GlobalUsings.g.cs", "// <auto-generated/>\nglobal using global::System;\nglobal using global::Microsoft.AspNetCore.Http;\n");
        const string path = "src/Web/Endpoints/Hobbies.cs";
        const string code = """
            using System.Text;

            namespace Shop.Web.Endpoints;

            public class Hobbies
            {
                [Test]
                public void Check() => 1.ShouldBe(1);

                public static Results<NoContent, NotFound> Delete() => TypedResults.NoContent();

                public static Ok<int> Get() => TypedResults.Ok(1);
            }

            """;
        Write(path, code);

        var fixes = await _fixer.FixAsync(_root,
        [
            Error(path, code, "CS0246", "Test"),
            Error(path, code, "CS0246", "Test"),                         // derleyici [Test] için "TestAttribute"u da bildirir
            Error(path, code, "CS1061", "ShouldBe"),                     // extension metodu
            Error(path, code, "CS0308", "Results"),                      // Microsoft.AspNetCore.Http.Results generic değil
            Error(path, code, "CS0246", "<NoContent", "NoContent"),
            Error(path, code, "CS0246", "NotFound>", "NotFound"),
            Error(path, code, "CS0246", "Ok<", "Ok"),
            Error(path, code, "CS0246", "System.Text", "Text")           // using satırındaki hata using eklenerek düzelmez
        ], CancellationToken.None);

        var fix = fixes.ShouldHaveSingleItem();
        fix.AddedNamespaces.ShouldBe(["Microsoft.AspNetCore.Http.HttpResults", "NUnit.Framework", "Shouldly"]);
        fix.Content.ShouldStartWith("using System.Text;\nusing Microsoft.AspNetCore.Http.HttpResults;\nusing NUnit.Framework;\nusing Shouldly;\n\nnamespace Shop.Web.Endpoints;");
        CSharpSyntaxTree.ParseText(fix.Content).GetDiagnostics().ShouldBeEmpty();
    }

    [Test]
    public void ShouldPlaceUsingsBeforeTheFirstDeclarationAndKeepLineEndings()
    {
        const string code = "// Başlık yorumu\r\nnamespace Shop;\r\n\r\npublic class A { }\r\n";
        var unit = CSharpSyntaxTree.ParseText(code).GetCompilationUnitRoot();

        UsingDirectiveFixer.InsertUsings(code, unit, ["Shop.Domain"])
            .ShouldBe("// Başlık yorumu\r\nusing Shop.Domain;\r\n\r\nnamespace Shop;\r\n\r\npublic class A { }\r\n");
    }

    private static CompilerDiagnostic Error(string path, string content, string code, string context, string? name = null)
    {
        var index = content.IndexOf(context, StringComparison.Ordinal) + context.IndexOf(name ?? context, StringComparison.Ordinal);
        var before = content[..index];
        var line = before.Count(c => c == '\n') + 1;
        var column = index - (before.LastIndexOf('\n') + 1) + 1;
        return new CompilerDiagnostic(path, line, column, code, $"'{name ?? context}' could not be found");
    }

    private static string Project(params string[] references) =>
        "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup>" +
        string.Concat(references.Select(reference => $"<ProjectReference Include=\"{reference}\" />")) + "</ItemGroup></Project>";

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static void CopyAssembly(string source, string directory)
    {
        Directory.CreateDirectory(directory);
        File.Copy(source, Path.Combine(directory, Path.GetFileName(source)));
    }

    private static bool FrameworkExists(string framework) =>
        Directory.Exists(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(typeof(object).Assembly.Location)))!, framework));
}
