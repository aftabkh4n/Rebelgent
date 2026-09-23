namespace Rebelgent.GitHub.Release;

/// <summary>
/// Parses and compares semantic versions in MAJOR.MINOR.PATCH format.
/// Handles optional leading "v" (e.g. "v1.2.3" == "1.2.3").
/// Malformed inputs are never thrown — they return false / null.
/// </summary>
internal static class SemverHelper
{
    public static bool TryParse(string? raw, out int major, out int minor, out int patch)
    {
        major = minor = patch = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var s = raw.Trim();
        if (s.StartsWith('v') || s.StartsWith('V'))
            s = s[1..];

        var parts = s.Split('.');
        if (parts.Length != 3) return false;

        return int.TryParse(parts[0], out major)
            && major >= 0
            && int.TryParse(parts[1], out minor)
            && minor >= 0
            && int.TryParse(parts[2], out patch)
            && patch >= 0;
    }

    /// <summary>Returns true if <paramref name="proposed"/> is strictly greater than <paramref name="baseline"/>.</summary>
    public static bool IsGreaterThan(string proposed, string baseline)
    {
        if (!TryParse(proposed, out var pMaj, out var pMin, out var pPat)) return false;
        if (!TryParse(baseline, out var bMaj, out var bMin, out var bPat)) return true;

        if (pMaj != bMaj) return pMaj > bMaj;
        if (pMin != bMin) return pMin > bMin;
        return pPat > bPat;
    }

    /// <summary>
    /// Returns the highest valid semver found in <paramref name="tags"/>, as a plain "MAJOR.MINOR.PATCH" string.
    /// Tags with leading "v" are normalised. Malformed tags are silently skipped.
    /// Returns null if no valid semver tags are found.
    /// </summary>
    public static string? FindLatest(IEnumerable<string?> tags)
    {
        string? latest = null;
        int lMaj = 0, lMin = 0, lPat = 0;

        foreach (var tag in tags)
        {
            if (!TryParse(tag, out var maj, out var min, out var pat)) continue;

            if (latest is null
                || maj > lMaj
                || (maj == lMaj && min > lMin)
                || (maj == lMaj && min == lMin && pat > lPat))
            {
                latest = $"{maj}.{min}.{pat}";
                lMaj = maj;
                lMin = min;
                lPat = pat;
            }
        }

        return latest;
    }
}
