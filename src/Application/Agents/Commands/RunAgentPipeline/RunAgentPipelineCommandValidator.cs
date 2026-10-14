using DevAgents.Application.Common.Exceptions;
using DevAgents.Application.Common.Interfaces;
using DevAgents.Application.Common.Validation;

namespace DevAgents.Application.Agents.Commands.RunAgentPipeline;

public class RunAgentPipelineCommandValidator : AbstractValidator<RunAgentPipelineCommand>
{
    public RunAgentPipelineCommandValidator(IWorkspaceFileSystem workspace, IArchitectureCatalog architectures)
    {
        RuleFor(c => c.Question)
            .NotEmpty().WithMessage("Soru boş olamaz.");

        // Hatalı proje yolunu, hiçbir model çağrısı yapmadan en başta yakalıyoruz.
        RuleFor(c => c.ProjectPath).MustBeUsableProjectPath(workspace);

        RuleFor(c => c.Architecture)
            .Must(id => architectures.Find(id!) is not null)
            .When(c => !string.IsNullOrWhiteSpace(c.Architecture))
            .WithMessage(c => $"Mimari bulunamadı: {c.Architecture}");

        // Kodu projeye uygulamak için ya mevcut bir proje ya da oluşturulacak yeni projenin adı gerekir.
        RuleFor(c => c)
            .Must(c => !string.IsNullOrWhiteSpace(c.ProjectPath) || !string.IsNullOrWhiteSpace(c.NewProjectName))
            .When(c => c.ApplyChanges)
            .WithName("ProjectPath")
            .WithMessage("Kodun yazılacağı proje klasörünü seçin ya da yeni proje adı verin.");

        When(c => !string.IsNullOrWhiteSpace(c.NewProjectName), () =>
        {
            RuleFor(c => c.NewProjectName)
                .Matches("^[A-Za-z][A-Za-z0-9_]{0,63}$")
                .WithMessage("Proje adı harfle başlamalı; yalnızca harf, rakam ve _ içerebilir (en fazla 64 karakter).");

            RuleFor(c => c.ApplyChanges)
                .Equal(true)
                .WithMessage("Yeni proje oluşturmak için 'projeye uygula' seçeneğini açın.");

            RuleFor(c => c.Architecture)
                .Must(id => !string.IsNullOrWhiteSpace(id) && architectures.Find(id)?.CanCreateProjects() == true)
                .WithMessage("Yeni proje için şablonu tanımlı bir mimari seçin.");

            // Üst klasör kullanılabilir olmalı ve bu adda bir klasör henüz bulunmamalı.
            RuleFor(c => c)
                .Custom((command, context) =>
                {
                    try
                    {
                        workspace.ResolveNewProjectDirectory(command.ProjectPath, command.NewProjectName!);
                    }
                    catch (ProjectPathException ex)
                    {
                        context.AddFailure("NewProjectName", ex.Message);
                    }
                });
        });
    }
}
