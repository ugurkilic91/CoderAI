namespace DevAgents.Application.Common.Text;

internal static class TextExtensions
{
    /// <summary>Cuts <paramref name="text"/> to <paramref name="maxLength"/> characters and marks the cut.</summary>
    public static string Truncate(this string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "\n... [truncated]";
}
