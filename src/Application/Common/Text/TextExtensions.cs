namespace DevAgents.Application.Common.Text;

internal static class TextExtensions
{
    /// <summary>Metni <paramref name="maxLength"/> karaktere kısaltır ve kesildiğini belirtir.</summary>
    public static string Truncate(this string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "\n... [truncated]";
}
