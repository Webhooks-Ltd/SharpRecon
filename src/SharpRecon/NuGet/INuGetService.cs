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

internal interface INuGetService
{
    Task<NuGetDownloadResult> DownloadPackageAsync(string packageId, string? version, CancellationToken ct);
    Task<IReadOnlyList<NuGetSearchResult>> SearchAsync(string query, int take, CancellationToken ct);
}
