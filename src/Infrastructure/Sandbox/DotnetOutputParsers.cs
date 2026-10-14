using System.Text.RegularExpressions;
using System.Xml.Linq;
using DevAgents.Domain.ValueObjects;

namespace DevAgents.Infrastructure.Sandbox;

/// <summary>dotnet build/restore çıktısındaki hata satırlarını ayrıştırır.</summary>
internal static partial class MsBuildOutputParser
{
    /// <summary>
    /// Örnek satırlar:
    /// <c>/workspace/src/A.cs(12,17): error CS0246: The type ... [/workspace/src/App.csproj]</c> ve
    /// <c>/workspace/src/App.csproj : error NU1101: Unable to find package X. [/workspace/App.slnx]</c>.
    /// Dosya yolları <paramref name="roots"/>'tan birine göre göreli hâle getirilir. MSBuild her hatayı iki kez
    /// (derleme sırasında ve özette) yazdığı için tekrarlar atılır.
    /// </summary>
    public static IReadOnlyList<CompilerDiagnostic> ParseErrors(string output, params string[] roots)
    {
        var errors = new List<CompilerDiagnostic>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in output.Split('\n'))
        {
            var match = SourceError().Match(line);
            CompilerDiagnostic diagnostic;
            if (match.Success)
            {
                diagnostic = new CompilerDiagnostic(Relative(match.Groups["file"].Value, roots),
                    int.Parse(match.Groups["line"].Value), int.Parse(match.Groups["column"].Value),
                    match.Groups["code"].Value, match.Groups["message"].Value.Trim());
            }
            else if ((match = ProjectError().Match(line)).Success)
            {
                diagnostic = new CompilerDiagnostic(Relative(match.Groups["file"].Value, roots), 0, 0,
                    match.Groups["code"].Value, match.Groups["message"].Value.Trim());
            }
            else
            {
                continue;
            }

            if (seen.Add(diagnostic.ToString()))
                errors.Add(diagnostic);
        }
        return errors;
    }

    private static string Relative(string path, string[] roots)
    {
        path = path.Trim().Replace('\\', '/');
        foreach (var root in roots.Select(r => r.Replace('\\', '/').TrimEnd('/')).Where(r => r.Length > 0))
        {
            if (path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
                return path[(root.Length + 1)..];
        }
        return path;
    }

    [GeneratedRegex(@"^\s*(?<file>[^\r\n(]+?)\((?<line>\d+),(?<column>\d+)(?:,\d+,\d+)?\):\s+error\s+(?<code>[A-Za-z]+\d+):\s*(?<message>.+?)(?:\s+\[[^\]\r\n]+\])?\s*$")]
    private static partial Regex SourceError();

    [GeneratedRegex(@"^\s*(?<file>[^\r\n]+?)\s+:\s+error\s+(?<code>[A-Za-z]+\d+):\s*(?<message>.+?)(?:\s+\[[^\]\r\n]+\])?\s*$")]
    private static partial Regex ProjectError();
}

/// <summary>dotnet test sonuçlarını okur: önce TRX dosyaları (yapısal), yoksa konsol özeti.</summary>
internal static partial class TestResultParser
{
    private static readonly XNamespace Trx = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    /// <summary>TRX dosyalarındaki sonuçların toplamı; hiç dosya yoksa <c>null</c>.</summary>
    public static TestRunResult? FromTrxFiles(IEnumerable<string> paths)
    {
        int total = 0, passed = 0, failed = 0, skipped = 0, files = 0;
        var failures = new List<TestFailure>();
        foreach (var path in paths)
        {
            var document = XDocument.Load(path);
            files++;
            foreach (var result in document.Descendants(Trx + "UnitTestResult"))
            {
                total++;
                switch ((string?)result.Attribute("outcome"))
                {
                    case "Passed":
                        passed++;
                        break;
                    case "Failed":
                        failed++;
                        var error = result.Element(Trx + "Output")?.Element(Trx + "ErrorInfo");
                        failures.Add(new TestFailure(
                            (string?)result.Attribute("testName") ?? "?",
                            error?.Element(Trx + "Message")?.Value.Trim() ?? "",
                            error?.Element(Trx + "StackTrace")?.Value.Trim()));
                        break;
                    default:
                        skipped++;
                        break;
                }
            }
        }
        return files == 0 ? null : new TestRunResult(total, passed, failed, skipped, failures, "", 0);
    }

    /// <summary>
    /// Konsol özeti: <c>Passed!  - Failed: 0, Passed: 5, Skipped: 0, Total: 5</c> satırları toplanır;
    /// başarısız testler "Failed Ad [süre]" + "Error Message:" bloklarından okunur.
    /// </summary>
    public static TestRunResult FromConsole(string output)
    {
        int total = 0, passed = 0, failed = 0, skipped = 0;
        foreach (Match match in Summary().Matches(output))
        {
            failed += int.Parse(match.Groups["failed"].Value);
            passed += int.Parse(match.Groups["passed"].Value);
            skipped += int.Parse(match.Groups["skipped"].Value);
            total += int.Parse(match.Groups["total"].Value);
        }

        var failures = FailedTest().Matches(output)
            .Select(match => new TestFailure(match.Groups["name"].Value.Trim(), match.Groups["message"].Value.Trim(), null))
            .ToList();
        return new TestRunResult(total, passed, failed, skipped, failures, "", 0);
    }

    [GeneratedRegex(@"(?:Passed|Failed)!\s+-\s+Failed:\s+(?<failed>\d+),\s+Passed:\s+(?<passed>\d+),\s+Skipped:\s+(?<skipped>\d+),\s+Total:\s+(?<total>\d+)")]
    private static partial Regex Summary();

    [GeneratedRegex(@"^\s*Failed (?<name>\S+) \[[^\]]*\]\s*\r?\n\s*Error Message:\s*\r?\n(?<message>(?:(?!\s*Stack Trace:)[^\n]*\n?){1,10})", RegexOptions.Multiline)]
    private static partial Regex FailedTest();
}
