using System.Text.RegularExpressions;

namespace Rebelgent.ClaudeCode.ReleaseNotes;

internal static class SemverValidator
{
    private static readonly Regex StrictPattern = new(@"^\d+\.\d+\.\d+$", RegexOptions.Compiled);
    private static readonly Regex ExtractPattern = new(@"\b(\d+\.\d+\.\d+)\b", RegexOptions.Compiled);

    public static bool IsValid(string version) =>
        !string.IsNullOrWhiteSpace(version) && StrictPattern.IsMatch(version.Trim());

    /// <summary>Finds the first N.N.N occurrence in text. Returns null if none found.</summary>
    public static string? TryExtract(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var match = ExtractPattern.Match(text);
        return match.Success ? match.Groups[1].Value : null;
    }
}
