using DevAgents.Application.Common.Interfaces;
using DevAgents.Application.Common.Validation;

namespace DevAgents.Application.Changes.Commands.RevertChanges;

public class RevertChangesCommandValidator : AbstractValidator<RevertChangesCommand>
{
    public RevertChangesCommandValidator(IWorkspaceFileSystem workspace)
    {
        RuleFor(c => c.ProjectPath)
            .NotEmpty().WithMessage("Proje klasörü belirtilmedi.")
            .MustBeUsableProjectPath(workspace);

        // Anlık görüntü kimliği bir git commit özeti: 40 karakter onaltılık sayı.
        RuleFor(c => c.Snapshot)
            .Matches("^[0-9a-f]{40}$").WithMessage("Geçersiz anlık görüntü kimliği.");
    }
}
