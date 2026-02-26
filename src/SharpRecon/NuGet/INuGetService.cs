namespace SharpRecon.NuGet;

internal record NuGetDownloadResult(
    string PackageId,
    string ResolvedVersion,
    string CachePath,
    IReadOnlyList<string> AvailableTfms);

internal interface INuGetService
{
    Task<NuGetDownloadResult> DownloadPackageAsync(string packageId, string? version, CancellationToken ct);
}
