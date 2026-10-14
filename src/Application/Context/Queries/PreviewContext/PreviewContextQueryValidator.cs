using DevAgents.Application.Common.Interfaces;
using DevAgents.Application.Common.Validation;

namespace DevAgents.Application.Context.Queries.PreviewContext;

public class PreviewContextQueryValidator : AbstractValidator<PreviewContextQuery>
{
    public PreviewContextQueryValidator(IWorkspaceFileSystem workspace)
    {
        // Verilen proje yolu izinli ve var olan bir klasör olmalı.
        RuleFor(q => q.ProjectPath).MustBeUsableProjectPath(workspace);
    }
}
