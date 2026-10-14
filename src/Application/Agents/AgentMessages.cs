using System.Text;
using DevAgents.Application.Common.Text;
using DevAgents.Application.Context;
using DevAgents.Domain.ValueObjects;

namespace DevAgents.Application.Agents;

/// <summary>
/// Her ajana giden kullanıcı mesajını kurar. Mesajlar İngilizcedir (modeller İngilizcede daha iyi sonuç veriyor).
/// "## ..." bölüm adlarına prompts/*.md dosyalarında atıf yapılıyor; değiştirirseniz ikisini birlikte değiştirin.
/// </summary>
internal sealed class AgentMessages
{
    private const int MaxHistoryCodeChars = 6000;
    private const int MaxFixFileChars = 12_000;
    private const int MaxFixFilesChars = 30_000;
    private const int MaxStackTraceLines = 6;

    private const string NoImplementation =
        "(No new code was generated. The code to work on is in the PROJECT CONTEXT / user-provided files.)";

    private const string RevisionInstruction =
        "Produce the corrected, COMPLETE implementation. Fix every BLOCKER and MAJOR issue. Use exactly the same output format.";

    private readonly string _request;
    private readonly string _history;
    private readonly string _notes;
    private readonly string _context;
    private readonly string _architecture;
    private readonly string _architectureRules;
    private readonly ArchitectureGuide? _guide;

    public AgentMessages(
        string englishRequest,
        ProjectContext context,
        string? extraContext,
        IReadOnlyList<HistoryItem>? history,
        int maxHistoryItems,
        ArchitectureGuide? architecture = null)
    {
        _request = englishRequest;
        _context = $"## PROJECT CONTEXT\n{context.Text}";
        _notes = string.IsNullOrWhiteSpace(extraContext)
            ? ""
            : $"\n\n## ADDITIONAL NOTES FROM THE USER (may be in Turkish)\n{extraContext}";
        _history = HistorySection(history, maxHistoryItems);
        // Mimari seçilmediyse bu bölümler boştur ve mesajlar mimarisiz akıştakiyle birebir aynı kalır.
        _architecture = string.IsNullOrEmpty(architecture?.FullText) ? "" : "\n\n" + architecture.FullText;
        _architectureRules = string.IsNullOrEmpty(architecture?.RulesText) ? "" : "\n\n" + architecture.RulesText;
        _guide = architecture;
    }

    public static string ForRouter(string englishRequest, bool hasProject) =>
        $"## REQUEST\n{englishRequest}\n\n## PROJECT PROVIDED\n{(hasProject ? "yes" : "no")}";

    public string ForAnswer() =>
        $"## QUESTION\n{_request}{_history}{_notes}\n\n{_context}";

    public string ForAnalysis() =>
        $"## REQUEST\n{_request}{_history}{_notes}{_architecture}\n\n{_context}";

    public string ForCoding(AgentOutputs work) =>
        $"## REQUEST\n{_request}{Analysis(work)}{_history}{_notes}{_architecture}\n\n{_context}";

    public string ForTesting(AgentOutputs work) =>
        $"## REQUEST\n{_request}{Analysis(work)}{Implementation(work)}{_notes}{_architecture}\n\n{_context}";

    /// <param name="verification">Kod projeye uygulandıysa gerçek derleme/test sonuçları (review ajanı derleme hatası aramasın).</param>
    public string ForReview(AgentOutputs work, string? verification = null) =>
        $"## REQUEST\n{_request}{Analysis(work)}{Implementation(work)}\n\n" +
        $"## TESTS\n{(work.Tests.Length > 0 ? work.Tests : "(none)")}" +
        (string.IsNullOrEmpty(verification) ? "" : $"\n\n## BUILD AND TEST RESULTS\n{verification}") +
        $"{_notes}{_architecture}\n\n{_context}";

    public string ForRevision(AgentOutputs work) =>
        $"## REQUEST\n{_request}{Analysis(work)}\n\n## PREVIOUS IMPLEMENTATION\n{work.Code}\n\n" +
        $"## REVIEW FEEDBACK\n{work.Review}\n\n{RevisionInstruction}{_architecture}\n\n{_context}";

    // --- Düzeltme ajanı (prompts/fixer.md) mesajları: yalnızca ilgili dosyalar ve hatalar; bağlam küçük tutulur. ---

    /// <summary>Roslyn'in bulduğu sözdizimi hataları için.</summary>
    public string ForSyntaxFix(IReadOnlyList<CompilerDiagnostic> errors, IReadOnlyList<GeneratedFile> files) =>
        "## TASK\nThe files below contain C# syntax errors found by the Roslyn parser. Fix the syntax errors and output the corrected files.\n\n" +
        $"## REQUEST\n{_request}\n\n## SYNTAX ERRORS\n{Bullets(errors)}\n\n## CURRENT FILES\n{FileBlocks(files)}{_architectureRules}";

    /// <summary>dotnet build hataları için: hatalar, ilgili dosyaların diskteki güncel hâli ve bu görevde değişen dosyalar.</summary>
    public string ForBuildFix(IReadOnlyList<CompilerDiagnostic> errors, IReadOnlyList<GeneratedFile> currentFiles, IEnumerable<string> changedPaths) =>
        "## TASK\nThe project does not compile after the changes made for the request. Fix ALL build errors below with minimal, targeted changes. " +
        "Keep the intent of the request and the architecture.\n\n" +
        $"## REQUEST\n{_request}\n\n## BUILD ERRORS\n{Bullets(errors)}\n\n## CURRENT FILES\n{FileBlocks(currentFiles)}\n\n" +
        $"## FILES CHANGED FOR THIS REQUEST\n{Bullets(changedPaths)}{_architectureRules}{Examples(errors.Select(error => error.File))}";

    /// <summary>Başarısız birim testleri için: hangi tarafın (kod mu test mi) hatalı olduğuna ajan karar verir.</summary>
    public string ForTestFix(TestRunResult result, IReadOnlyList<GeneratedFile> currentFiles) =>
        "## TASK\nSome unit tests fail. For each failure decide whether the implementation or the test is wrong " +
        "(the REQUEST is the source of truth) and fix that file. Do not delete or weaken tests just to make them pass.\n\n" +
        $"## REQUEST\n{_request}\n\n## FAILING TESTS ({result.Failed} of {result.Total})\n{Failures(result.Failures)}\n\n" +
        $"## CURRENT FILES\n{FileBlocks(currentFiles)}{_architectureRules}" +
        Examples(currentFiles.Where(file => file.Kind == GeneratedFileKind.Test).Select(file => file.Path));

    // Hatalı dosyaların bölümündeki mimari örnekleri (doğru using'ler, test kurulumu); mimari seçilmediyse boş.
    private string Examples(IEnumerable<string> paths)
    {
        var examples = _guide?.ExamplesFor(paths);
        return string.IsNullOrEmpty(examples)
            ? ""
            : $"\n\n## REFERENCE FILES OF THE ARCHITECTURE (copy their usings, base classes and test setup)\n{examples}";
    }

    private static string Analysis(AgentOutputs work) =>
        work.Analysis.Length > 0 ? $"\n\n## ANALYSIS\n{work.Analysis}" : "";

    // Yeni kod yoksa test ve review ajanları bağlamdaki kod üzerinde çalışır ("şunun testlerini yaz" gibi).
    private static string Implementation(AgentOutputs work) =>
        work.Code.Length > 0 || work.IsCodeRevised
            ? $"\n\n## IMPLEMENTATION\n{work.Code}"
            : $"\n\n## IMPLEMENTATION\n{NoImplementation}";

    // Son birkaç sohbet turu: önceki istek, üretilen kod ve ajanın sorduğu sorular (kullanıcı onlara cevap veriyor olabilir).
    private static string HistorySection(IReadOnlyList<HistoryItem>? history, int maxItems)
    {
        if (history is null || history.Count == 0 || maxItems <= 0)
            return "";

        var section = new StringBuilder("\n\n## CONVERSATION HISTORY (previous turns, oldest first)\n");
        foreach (var turn in history.Where(turn => turn is not null).TakeLast(maxItems))
        {
            section.AppendLine($"Previous request: {turn.Request}");
            if (!string.IsNullOrWhiteSpace(turn.Code))
                section.AppendLine($"Previous implementation:\n{turn.Code.Truncate(MaxHistoryCodeChars)}");
            if (!string.IsNullOrWhiteSpace(turn.Questions))
                section.AppendLine($"Questions you asked the user (the new request may answer them):\n{turn.Questions}");
            section.AppendLine();
        }
        return section.ToString().TrimEnd();
    }

    private static string Bullets<T>(IEnumerable<T> items) => string.Join("\n", items.Select(item => $"- {item}"));

    private static string Failures(IEnumerable<TestFailure> failures) => string.Join("\n", failures.Select(failure =>
    {
        var stack = string.Join("\n", (failure.StackTrace ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(MaxStackTraceLines).Select(line => "    " + line.Trim()));
        return $"- {failure.Name}\n  {failure.Message.Trim()}" + (stack.Length > 0 ? "\n" + stack : "");
    }));

    // Dosyalar sırayla eklenir; toplam bütçe dolunca kalanlar atlanır (ilk sıradakiler hatalı dosyalardır).
    private static string FileBlocks(IEnumerable<GeneratedFile> files)
    {
        var blocks = new StringBuilder();
        foreach (var file in files)
        {
            var block = $"<file path=\"{file.Path}\">\n{file.Content.Truncate(MaxFixFileChars)}\n</file>\n";
            if (blocks.Length > 0 && blocks.Length + block.Length > MaxFixFilesChars)
                break;
            blocks.Append(block);
        }
        return blocks.ToString().TrimEnd();
    }
}
