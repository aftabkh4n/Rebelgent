using Rebelgent.GitHub.Release;

namespace Rebelgent.GitHub.Tests;

public class SemverHelperTests
{
    // ── TryParse ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1.0.0", 1, 0, 0)]
    [InlineData("2.3.4", 2, 3, 4)]
    [InlineData("0.0.1", 0, 0, 1)]
    [InlineData("10.20.30", 10, 20, 30)]
    public void TryParse_ValidVersion_ReturnsTrue(string raw, int expectedMaj, int expectedMin, int expectedPat)
    {
        var result = SemverHelper.TryParse(raw, out var maj, out var min, out var pat);

        Assert.True(result);
        Assert.Equal(expectedMaj, maj);
        Assert.Equal(expectedMin, min);
        Assert.Equal(expectedPat, pat);
    }

    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("V0.0.1", 0, 0, 1)]
    [InlineData("v10.0.0", 10, 0, 0)]
    public void TryParse_StripsLeadingV(string raw, int expectedMaj, int expectedMin, int expectedPat)
    {
        var result = SemverHelper.TryParse(raw, out var maj, out var min, out var pat);

        Assert.True(result);
        Assert.Equal(expectedMaj, maj);
        Assert.Equal(expectedMin, min);
        Assert.Equal(expectedPat, pat);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1.0")]
    [InlineData("1.0.0.0")]
    [InlineData("abc")]
    [InlineData("1.x.0")]
    [InlineData("-1.0.0")]
    [InlineData("1.-2.0")]
    public void TryParse_Malformed_ReturnsFalse(string? raw)
    {
        var result = SemverHelper.TryParse(raw, out _, out _, out _);

        Assert.False(result);
    }

    // ── IsGreaterThan ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2.0.0", "1.0.0")]
    [InlineData("1.1.0", "1.0.0")]
    [InlineData("1.0.1", "1.0.0")]
    [InlineData("1.1.1", "1.1.0")]
    public void IsGreaterThan_HigherVersion_ReturnsTrue(string proposed, string baseline)
    {
        Assert.True(SemverHelper.IsGreaterThan(proposed, baseline));
    }

    [Theory]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("0.9.9", "1.0.0")]
    [InlineData("1.0.0", "1.0.1")]
    [InlineData("1.0.0", "2.0.0")]
    public void IsGreaterThan_SameOrLowerVersion_ReturnsFalse(string proposed, string baseline)
    {
        Assert.False(SemverHelper.IsGreaterThan(proposed, baseline));
    }

    [Fact]
    public void IsGreaterThan_MalformedProposed_ReturnsFalse()
    {
        Assert.False(SemverHelper.IsGreaterThan("not-a-version", "1.0.0"));
    }

    [Fact]
    public void IsGreaterThan_MalformedBaseline_ReturnsTrue()
    {
        // If we can't parse the baseline, treat proposed as greater (no known ceiling)
        Assert.True(SemverHelper.IsGreaterThan("1.0.0", "not-a-version"));
    }

    [Fact]
    public void IsGreaterThan_HandlesLeadingV()
    {
        Assert.True(SemverHelper.IsGreaterThan("1.1.0", "v1.0.0"));
        Assert.True(SemverHelper.IsGreaterThan("v1.1.0", "1.0.0"));
    }

    // ── FindLatest ────────────────────────────────────────────────────────────

    [Fact]
    public void FindLatest_ReturnsHighestSemver()
    {
        var tags = new[] { "v1.0.0", "v1.2.3", "v1.1.0" };
        Assert.Equal("1.2.3", SemverHelper.FindLatest(tags));
    }

    [Fact]
    public void FindLatest_MixedWithMalformed_SkipsMalformed()
    {
        var tags = new[] { "v1.0.0", "not-valid", "v0.9.0", null, "", "v1.0.1" };
        Assert.Equal("1.0.1", SemverHelper.FindLatest(tags));
    }

    [Fact]
    public void FindLatest_AllMalformed_ReturnsNull()
    {
        var tags = new[] { "latest", "stable", null, "" };
        Assert.Null(SemverHelper.FindLatest(tags));
    }

    [Fact]
    public void FindLatest_EmptyCollection_ReturnsNull()
    {
        Assert.Null(SemverHelper.FindLatest([]));
    }

    [Fact]
    public void FindLatest_SingleEntry_ReturnsThatVersion()
    {
        Assert.Equal("2.5.0", SemverHelper.FindLatest(["v2.5.0"]));
    }

    [Fact]
    public void FindLatest_NormalisesLeadingV_ReturnsWithoutV()
    {
        var result = SemverHelper.FindLatest(["v3.1.0"]);
        Assert.Equal("3.1.0", result);
        Assert.DoesNotContain("v", result!);
    }
}
