using System.Text;
using DevAgents.Domain.ValueObjects;

namespace DevAgents.Application.Agents.Development;

/// <summary>
/// Geliştirme akışının Türkçe özeti: hangi dosyalar eklendi/değişti, derleme ve testler ne durumda, nasıl geri alınır.
/// Model değil kod yazar; çeviriye gerek yoktur ve sayılar her zaman doğrudur.
/// </summary>
internal static class DevelopmentReport
{
    private const int MaxListedErrors = 10;

    public static string Render(DevelopmentSummary summary)
    {
        var report = new StringBuilder("## Geliştirme özeti\n\n");
        report.Append($"**Proje:** `{summary.ProjectPath}`");
        report.Append(summary.ProjectCreated ? " (bu istekte şablondan oluşturuldu)\n" : "\n");
        if (summary.Architecture is not null)
            report.Append($"**Mimari:** {summary.Architecture}\n");
        report.Append('\n');

        if (summary.Changes.Count > 0)
        {
            var created = summary.Changes.Count(c => c.Kind == FileChangeKind.Created);
            var modified = summary.Changes.Count(c => c.Kind == FileChangeKind.Modified);
            var deleted = summary.Changes.Count(c => c.Kind == FileChangeKind.Deleted);
            report.Append($"**Dosyalar:** {created} yeni, {modified} değişti" + (deleted > 0 ? $", {deleted} silindi" : "") + "\n");
            foreach (var change in summary.Changes)
                report.Append($"- {Symbol(change.Kind)} `{change.Path}`\n");
            report.Append('\n');
        }

        if (summary.Build is { } build)
        {
            var attempts = summary.BuildAttempts > 1 ? $" ({summary.BuildAttempts} derleme; hatalar düzeltme ajanıyla giderildi)" : "";
            report.Append(build.Succeeded
                ? $"**Build:** ✅ başarılı{attempts}\n"
                : $"**Build:** ❌ {build.Errors.Count} hata ({summary.BuildAttempts} derleme denendi)\n");
            foreach (var error in build.Errors.Take(MaxListedErrors))
                report.Append($"- `{error}`\n");
        }

        if (summary.Tests is { } tests)
        {
            var attempts = summary.TestAttempts > 1 ? $" ({summary.TestAttempts}. çalıştırmada)" : "";
            report.Append(tests.AllPassed()
                ? $"**Testler:** ✅ {tests.Passed}/{tests.Total} başarılı{attempts}\n"
                : $"**Testler:** ❌ {tests.Failed} başarısız, {tests.Passed}/{tests.Total} başarılı\n");
            foreach (var failure in tests.Failures.Take(MaxListedErrors))
                report.Append($"- `{failure.Name}`: {FirstLine(failure.Message)}\n");
        }

        if (summary.Problem is not null)
            report.Append($"\n⚠️ {summary.Problem}\n");

        if (summary.Snapshot is not null && summary.Changes.Count > 0)
            report.Append($"\nDeğişiklikleri geri almak için arayüzdeki **Değişiklikleri geri al** düğmesini kullanın (anlık görüntü `{summary.Snapshot[..Math.Min(8, summary.Snapshot.Length)]}`).\n");

        return report.ToString().TrimEnd();
    }

    private static string Symbol(string kind) => kind switch
    {
        FileChangeKind.Created => "🆕",
        FileChangeKind.Deleted => "🗑️",
        _ => "✏️"
    };

    private static string FirstLine(string text) => text.Split('\n', 2)[0].Trim();
}
