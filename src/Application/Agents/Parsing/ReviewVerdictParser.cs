using System.Text.RegularExpressions;

namespace DevAgents.Application.Agents.Parsing;

/// <summary>Review ajanının karar satırını okur (biçim prompts/reviewer.md'de tanımlı).</summary>
internal static partial class ReviewVerdictParser
{
    // Karar en üstte olmalı; review'un ilerisinde alıntılanan bir "VERDICT" sayılmaz.
    private const int VerdictSearchLength = 400;

    /// <summary>Yalnızca açık bir "VERDICT: APPROVED" onaydır; karar yoksa onaylanmamış sayılır.</summary>
    public static bool IsApproved(string review)
    {
        var head = review.Length > VerdictSearchLength ? review[..VerdictSearchLength] : review;
        if (NeedsChanges().IsMatch(head))
            return false;
        return Approved().IsMatch(head);
    }

    [GeneratedRegex(@"VERDICT:\s*NEEDS[_ ]CHANGES", RegexOptions.IgnoreCase)]
    private static partial Regex NeedsChanges();

    [GeneratedRegex(@"VERDICT:\s*APPROVED", RegexOptions.IgnoreCase)]
    private static partial Regex Approved();
}
