using global::NuGet.Versioning;
using SharpRecon.NuGet;
using Shouldly;
using Xunit;

namespace SharpRecon.Tests.NuGet;

public sealed class NuGetServiceTests
{
    [Fact]
    public void ParseVersionPattern_MajorWildcard_ReturnsCorrectRange()
    {
        var range = NuGetService.ParseVersionPattern("2.*");

        range.MinVersion.ShouldBe(new NuGetVersion(2, 0, 0));
        range.IsMinInclusive.ShouldBeTrue();
        range.MaxVersion.ShouldBe(new NuGetVersion(3, 0, 0));
        range.IsMaxInclusive.ShouldBeFalse();
    }

    [Fact]
    public void ParseVersionPattern_MajorMinorWildcard_ReturnsCorrectRange()
    {
        var range = NuGetService.ParseVersionPattern("2.1.*");

        range.MinVersion.ShouldBe(new NuGetVersion(2, 1, 0));
        range.IsMinInclusive.ShouldBeTrue();
        range.MaxVersion.ShouldBe(new NuGetVersion(2, 2, 0));
        range.IsMaxInclusive.ShouldBeFalse();
    }

    [Fact]
    public void ParseVersionPattern_ExactVersion_PassesThrough()
    {
        var range = NuGetService.ParseVersionPattern("13.0.3");

        range.MinVersion.ShouldBe(new NuGetVersion(13, 0, 3));
        range.IsMinInclusive.ShouldBeTrue();
        range.MaxVersion.ShouldBe(new NuGetVersion(13, 0, 3));
        range.IsMaxInclusive.ShouldBeTrue();
    }

    [Fact]
    public void ParseVersionPattern_NullOrEmpty_ReturnsAll()
    {
        var rangeNull = NuGetService.ParseVersionPattern(null);
        var rangeEmpty = NuGetService.ParseVersionPattern("");
        var rangeWhitespace = NuGetService.ParseVersionPattern("   ");

        rangeNull.ShouldBe(VersionRange.All);
        rangeEmpty.ShouldBe(VersionRange.All);
        rangeWhitespace.ShouldBe(VersionRange.All);
    }

    [Fact]
    public void ParseVersionPattern_InvalidFormat_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() => NuGetService.ParseVersionPattern("not.a.version"));
    }

    [Fact]
    public void ParseVersionPattern_13Star_MatchesVersionsInRange()
    {
        var range = NuGetService.ParseVersionPattern("13.*");

        range.Satisfies(new NuGetVersion(13, 0, 0)).ShouldBeTrue();
        range.Satisfies(new NuGetVersion(13, 0, 3)).ShouldBeTrue();
        range.Satisfies(new NuGetVersion(13, 99, 99)).ShouldBeTrue();
        range.Satisfies(new NuGetVersion(12, 0, 0)).ShouldBeFalse();
        range.Satisfies(new NuGetVersion(14, 0, 0)).ShouldBeFalse();
    }

    [Fact]
    public void ParseVersionPattern_MajorMinorWildcard_MatchesVersionsInRange()
    {
        var range = NuGetService.ParseVersionPattern("2.1.*");

        range.Satisfies(new NuGetVersion(2, 1, 0)).ShouldBeTrue();
        range.Satisfies(new NuGetVersion(2, 1, 5)).ShouldBeTrue();
        range.Satisfies(new NuGetVersion(2, 0, 0)).ShouldBeFalse();
        range.Satisfies(new NuGetVersion(2, 2, 0)).ShouldBeFalse();
    }

    [Fact]
    public void ParseVersionPattern_DoubleWildcard_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() => NuGetService.ParseVersionPattern("*.*"));
    }

    [Fact]
    public void ParseVersionPattern_JustStar_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() => NuGetService.ParseVersionPattern("*"));
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(1_000, "1K")]
    [InlineData(1_500, "1.5K")]
    [InlineData(12_345, "12.3K")]
    [InlineData(1_000_000, "1M")]
    [InlineData(12_345_678, "12.3M")]
    [InlineData(1_000_000_000, "1B")]
    [InlineData(2_100_000_000, "2.1B")]
    public void FormatDownloadCount_FormatsCorrectly(long count, string expected)
    {
        NuGetService.FormatDownloadCount(count).ShouldBe(expected);
    }
}
