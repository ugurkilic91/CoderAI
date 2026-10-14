using DevAgents.Application.Agents.Commands.RunAgentPipeline;
using DevAgents.Application.UnitTests.Fakes;
using NUnit.Framework;
using Shouldly;

namespace DevAgents.Application.UnitTests.Agents.Commands;

public class RunAgentPipelineCommandValidatorTests
{
    private readonly RunAgentPipelineCommandValidator _validator = new(new FakeWorkspace(), new FakeArchitectureCatalog());

    private async Task<string[]> ErrorsOf(RunAgentPipelineCommand command) =>
        [.. (await _validator.ValidateAsync(command)).Errors.Select(e => e.ErrorMessage)];

    [TestCase("")]
    [TestCase("   ")]
    public async Task ShouldRequireAQuestion(string question)
    {
        (await ErrorsOf(new RunAgentPipelineCommand { Question = question })).ShouldBe(["Soru boş olamaz."]);
    }

    [Test]
    public async Task ShouldRejectAProjectPathThatCannotBeUsed()
    {
        (await ErrorsOf(new RunAgentPipelineCommand { Question = "x", ProjectPath = "missing" }))
            .ShouldBe(["Proje klasörü bulunamadı: missing"]);
    }

    [Test]
    public async Task ShouldAcceptAQuestionWithAUsableProject()
    {
        (await ErrorsOf(new RunAgentPipelineCommand { Question = "x", ProjectPath = "Shop" })).ShouldBeEmpty();
    }

    [Test]
    public async Task ShouldRejectAnUnknownArchitecture()
    {
        (await ErrorsOf(new RunAgentPipelineCommand { Question = "x", Architecture = "onion" }))
            .ShouldBe(["Mimari bulunamadı: onion"]);
    }

    [Test]
    public async Task ShouldNeedAProjectToApplyChangesTo()
    {
        (await ErrorsOf(new RunAgentPipelineCommand { Question = "x", ApplyChanges = true }))
            .ShouldBe(["Kodun yazılacağı proje klasörünü seçin ya da yeni proje adı verin."]);
    }

    [Test]
    public async Task ShouldAcceptANewProjectFromAnArchitectureWithATemplate()
    {
        (await ErrorsOf(new RunAgentPipelineCommand
        {
            Question = "x", ApplyChanges = true, Architecture = "clean-architecture", NewProjectName = "HobbyTracker"
        })).ShouldBeEmpty();
    }

    [TestCase("1Hobby")]
    [TestCase("Hobby Tracker")]
    [TestCase("../Hobby")]
    public async Task ShouldRejectAnUnsafeProjectName(string name)
    {
        (await ErrorsOf(new RunAgentPipelineCommand { Question = "x", ApplyChanges = true, Architecture = "clean-architecture", NewProjectName = name }))
            .ShouldContain("Proje adı harfle başlamalı; yalnızca harf, rakam ve _ içerebilir (en fazla 64 karakter).");
    }

    [Test]
    public async Task ShouldRequireApplyAndAnArchitectureForANewProject()
    {
        var errors = await ErrorsOf(new RunAgentPipelineCommand { Question = "x", NewProjectName = "HobbyTracker" });

        errors.ShouldContain("Yeni proje oluşturmak için 'projeye uygula' seçeneğini açın.");
        errors.ShouldContain("Yeni proje için şablonu tanımlı bir mimari seçin.");
    }

    [Test]
    public async Task ShouldRejectANewProjectThatAlreadyExists()
    {
        (await ErrorsOf(new RunAgentPipelineCommand { Question = "x", ApplyChanges = true, Architecture = "clean-architecture", NewProjectName = "Existing" }))
            .ShouldBe(["Bu adda bir klasör zaten var: /projects/Existing"]);
    }
}
