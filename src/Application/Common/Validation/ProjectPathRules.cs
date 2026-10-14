using DevAgents.Application.Common.Exceptions;
using DevAgents.Application.Common.Interfaces;

namespace DevAgents.Application.Common.Validation;

/// <summary>Komut ve sorgu doğrulayıcılarının ortak proje yolu kuralı.</summary>
public static class ProjectPathRules
{
    /// <summary>
    /// Yol izinli bir kökün altında ve var olan bir klasör olmalı. Kontrolü, asıl çözümlemeyi yapan dosya sistemi
    /// katmanına bırakıyoruz; hata mesajı (Türkçe) oradan gelir. Böylece hatalı yol, hiçbir model çağrısı
    /// yapılmadan istek başında yakalanır.
    /// </summary>
    public static IRuleBuilderOptionsConditions<T, string?> MustBeUsableProjectPath<T>(
        this IRuleBuilder<T, string?> rule, IWorkspaceFileSystem workspace) =>
        rule.Custom((path, context) =>
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            try
            {
                workspace.ResolveProjectPath(path);
            }
            catch (ProjectPathException ex)
            {
                context.AddFailure(ex.Message);
            }
        });
}
