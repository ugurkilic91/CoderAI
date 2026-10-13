using System.Text.RegularExpressions;

namespace DevAgents.Application.Chat.Parsing;

/// <summary>Reads the reviewer's verdict line (format defined in prompts/reviewer.md).</summary>
internal static partial class ReviewVerdictParser
{
    // The verdict belongs at the top; a verdict quoted later in the review does not count.
    private const int VerdictSearchLength = 400;

    /// <summary>True only for an explicit "VERDICT: APPROVED"; a missing verdict counts as not approved.</summary>
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
