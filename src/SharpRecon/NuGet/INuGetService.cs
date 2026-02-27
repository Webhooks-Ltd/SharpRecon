using global::NuGet.Versioning;

namespace SharpRecon.NuGet;

internal record NuGetDownloadResult(
    string PackageId,
    string ResolvedVersion,
    string CachePath,
    IReadOnlyList<string> AvailableTfms);

internal record NuGetSearchResult(
    string PackageId,
    string Version,
    string Description,
    long TotalDownloads,
    bool Verified);

internal record PackageHealthInfo(
    DateTimeOffset? Published,
    DeprecationInfo? Deprecation,
    IReadOnlyList<VulnerabilityInfo> Vulnerabilities);

internal record DeprecationInfo(
    IReadOnlyList<string> Reasons,
    string? Message,
    AlternatePackageInfo? AlternatePackage);

internal record AlternatePackageInfo(
    string PackageId,
    string VersionRange);

internal record VulnerabilityInfo(
    string Severity,
    Uri AdvisoryUrl);

internal interface INuGetService
{
    Task<NuGetDownloadResult> DownloadPackageAsync(string packageId, string? version, CancellationToken ct);
    Task<PackageHealthInfo> GetPackageHealthAsync(string packageId, NuGetVersion resolvedVersion, CancellationToken ct);
    Task<IReadOnlyList<NuGetSearchResult>> SearchAsync(string query, int take, CancellationToken ct);
}
