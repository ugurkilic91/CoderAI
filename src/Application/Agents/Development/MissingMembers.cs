using System.Text.RegularExpressions;
using DevAgents.Application.Common.Models;
using DevAgents.Domain.ValueObjects;

namespace DevAgents.Application.Agents.Development;

/// <summary>
/// Derleme hatalarından "şu tipte şu üye yok" bilgisini çıkarır. Mesajlar İngilizcedir: sandbox derleyiciyi
/// DOTNET_CLI_UI_LANGUAGE=en ile çalıştırır.
/// </summary>
internal static partial class MissingMembers
{
    /// <param name="writtenInThisRun">
    /// Bu görevde yazılan dosyalar. "Üye yok" hatası yalnızca daha önce var olan kodda geçiyorsa geri ekleme yapılır:
    /// ajanın kendi yeni kodu silinen bir üyeyi kullanıyorsa niyet belirsizdir (örn. yeniden adlandırma), onu düzeltme ajanı çözer.
    /// </param>
    public static IReadOnlyList<MissingMember> From(IEnumerable<CompilerDiagnostic> errors, Func<string, bool> writtenInThisRun) =>
    [
        .. errors
            .Select(error => error.Code switch
            {
                // CS1061 / CS0117: 'IApplicationDbContext' does not contain a definition for 'TodoLists' ...
                "CS1061" or "CS0117" when !writtenInThisRun(error.File) && NoDefinition().Match(error.Message) is { Success: true } match =>
                    new MissingMember(SimpleName(match.Groups["type"].Value), match.Groups["member"].Value),
                // CS0535: 'ApplicationDbContext' does not implement interface member 'IApplicationDbContext.TodoLists'
                "CS0535" when NotImplemented().Match(error.Message) is { Success: true } match =>
                    new MissingMember(SimpleName(match.Groups["type"].Value), match.Groups["member"].Value),
                _ => null
            })
            .OfType<MissingMember>()
            .Distinct()
    ];

    // "HobbyTracker.Application.IFoo<T>" → "IFoo"
    private static string SimpleName(string type) => type.Split('<')[0].Split('.')[^1];

    [GeneratedRegex("^'(?<type>[^']+)' does not contain a definition for '(?<member>[^']+)'")]
    private static partial Regex NoDefinition();

    [GeneratedRegex(@"^'(?<type>[^']+)' does not implement interface member '(?:[^']*\.)?(?<member>[^'.(]+)")]
    private static partial Regex NotImplemented();
}
