using System.Text;
using SharpRecon.NuGet;
using Shouldly;
using Xunit;

namespace SharpRecon.Tests.NuGet;

public sealed class NuGetDownloadToolTests
{
    [Fact]
    public void FormatHealth_HealthyPackage_EmitsPositiveSignal()
    {
        var health = new PackageHealthInfo(
            new DateTimeOffset(2023, 3, 8, 0, 0, 0, TimeSpan.Zero),
            null,
            []);

        var sb = new StringBuilder();
        NuGetDownloadTool.FormatHealth(health, sb);
        var output = sb.ToString();

        output.ShouldContain("Published: 2023-03-08");
        output.ShouldContain("No deprecation notices.");
        output.ShouldContain("No known vulnerabilities.");
    }

    [Fact]
    public void FormatHealth_DeprecatedWithAlternate_EmitsDetails()
    {
        var health = new PackageHealthInfo(
            new DateTimeOffset(2018, 11, 27, 0, 0, 0, TimeSpan.Zero),
            new DeprecationInfo(
                ["Legacy"],
                "Use Azure.Storage.Blobs instead",
                new AlternatePackageInfo("Azure.Storage.Blobs", "[12.0.0, )")),
            []);

        var sb = new StringBuilder();
        NuGetDownloadTool.FormatHealth(health, sb);
        var output = sb.ToString();

        output.ShouldContain("DEPRECATED (Legacy): \"Use Azure.Storage.Blobs instead\"");
        output.ShouldContain("Alternate: Azure.Storage.Blobs [12.0.0, )");
        output.ShouldContain("No known vulnerabilities.");
    }

    [Fact]
    public void FormatHealth_DeprecatedWithoutAlternate_OmitsAlternateLine()
    {
        var health = new PackageHealthInfo(
            new DateTimeOffset(2019, 5, 1, 0, 0, 0, TimeSpan.Zero),
            new DeprecationInfo(
                ["CriticalBugs"],
                "This package has critical security issues",
                null),
            []);

        var sb = new StringBuilder();
        NuGetDownloadTool.FormatHealth(health, sb);
        var output = sb.ToString();

        output.ShouldContain("DEPRECATED (CriticalBugs): \"This package has critical security issues\"");
        output.ShouldNotContain("Alternate:");
    }

    [Fact]
    public void FormatHealth_VulnerablePackage_ListsAdvisories()
    {
        var health = new PackageHealthInfo(
            new DateTimeOffset(2021, 8, 10, 0, 0, 0, TimeSpan.Zero),
            null,
            [
                new VulnerabilityInfo("High", new Uri("https://github.com/advisories/GHSA-xxx")),
                new VulnerabilityInfo("Moderate", new Uri("https://github.com/advisories/GHSA-yyy")),
            ]);

        var sb = new StringBuilder();
        NuGetDownloadTool.FormatHealth(health, sb);
        var output = sb.ToString();

        output.ShouldContain("No deprecation notices.");
        output.ShouldContain("Vulnerabilities:");
        output.ShouldContain("- HIGH: https://github.com/advisories/GHSA-xxx");
        output.ShouldContain("- MODERATE: https://github.com/advisories/GHSA-yyy");
    }

    [Fact]
    public void FormatHealth_DeprecatedAndVulnerable_EmitsBoth()
    {
        var health = new PackageHealthInfo(
            new DateTimeOffset(2018, 6, 1, 0, 0, 0, TimeSpan.Zero),
            new DeprecationInfo(["Legacy"], "Replaced by NewLib", null),
            [new VulnerabilityInfo("Critical", new Uri("https://example.com/advisory"))]);

        var sb = new StringBuilder();
        NuGetDownloadTool.FormatHealth(health, sb);
        var output = sb.ToString();

        output.ShouldContain("DEPRECATED");
        output.ShouldContain("Vulnerabilities:");
        output.ShouldContain("- CRITICAL:");
    }

    [Fact]
    public void FormatHealth_NullHealth_EmitsUnavailableMessage()
    {
        var sb = new StringBuilder();
        NuGetDownloadTool.FormatHealth(null, sb);
        var output = sb.ToString();

        output.ShouldContain("Health: unavailable (metadata query failed)");
    }

    [Fact]
    public void FormatHealth_NullPublished_RendersUnknown()
    {
        var health = new PackageHealthInfo(null, null, []);

        var sb = new StringBuilder();
        NuGetDownloadTool.FormatHealth(health, sb);
        var output = sb.ToString();

        output.ShouldContain("Published: unknown");
    }
}
