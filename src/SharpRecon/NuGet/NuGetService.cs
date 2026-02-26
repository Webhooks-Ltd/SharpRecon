using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using global::NuGet.Packaging.Core;
using global::NuGet.Protocol;
using global::NuGet.Protocol.Core.Types;
using global::NuGet.Versioning;
using SharpRecon.Infrastructure;

namespace SharpRecon.NuGet;

internal sealed partial class NuGetService : INuGetService
{
    private const string NuGetSourceUrl = "https://api.nuget.org/v3/index.json";

    private readonly IPackageCache _packageCache;

    public NuGetService(IPackageCache packageCache)
    {
        _packageCache = packageCache;
    }

    public async Task<NuGetDownloadResult> DownloadPackageAsync(string packageId, string? version, CancellationToken ct)
    {
        var repository = Repository.Factory.GetCoreV3(NuGetSourceUrl);
        var nugetLogger = global::NuGet.Common.NullLogger.Instance;
        var cacheContext = new SourceCacheContext();

        var resolvedVersion = await ResolveVersionAsync(repository, packageId, version, cacheContext, nugetLogger, ct);

        var normalizedVersion = resolvedVersion.ToNormalizedString();

        if (!_packageCache.IsPackageCached(packageId, normalizedVersion))
        {
            await DownloadToGlobalCacheAsync(repository, packageId, resolvedVersion, cacheContext, nugetLogger, ct);
        }

        var cachePath = _packageCache.GetPackagePath(packageId, normalizedVersion);
        var tfms = _packageCache.GetAvailableTfms(packageId, normalizedVersion);

        return new NuGetDownloadResult(packageId, normalizedVersion, cachePath, tfms);
    }

    internal static VersionRange ParseVersionPattern(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return VersionRange.All;

        if (NuGetVersion.TryParse(version, out var exactVersion))
            return new VersionRange(exactVersion, includeMinVersion: true, exactVersion, includeMaxVersion: true);

        var match = WildcardPattern().Match(version);
        if (!match.Success)
            throw new ArgumentException(
                $"Invalid version format: '{version}'. Use exact version (e.g. '13.0.3'), wildcard (e.g. '13.*'), or omit for latest.");

        var parts = match.Groups[1].Value.Split('.');

        return parts.Length switch
        {
            1 when int.TryParse(parts[0], out var major) =>
                new VersionRange(
                    new NuGetVersion(major, 0, 0), includeMinVersion: true,
                    new NuGetVersion(major + 1, 0, 0), includeMaxVersion: false),

            2 when int.TryParse(parts[0], out var major) && int.TryParse(parts[1], out var minor) =>
                new VersionRange(
                    new NuGetVersion(major, minor, 0), includeMinVersion: true,
                    new NuGetVersion(major, minor + 1, 0), includeMaxVersion: false),

            _ => throw new ArgumentException(
                $"Invalid version format: '{version}'. Use exact version (e.g. '13.0.3'), wildcard (e.g. '13.*'), or omit for latest.")
        };
    }

    private static async Task<NuGetVersion> ResolveVersionAsync(
        SourceRepository repository,
        string packageId,
        string? version,
        SourceCacheContext cacheContext,
        global::NuGet.Common.ILogger logger,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(version) && NuGetVersion.TryParse(version, out var exactVersion))
            return exactVersion;

        var metadataResource = await repository.GetResourceAsync<PackageMetadataResource>(ct);
        var allMetadata = await metadataResource.GetMetadataAsync(
            packageId,
            includePrerelease: false,
            includeUnlisted: false,
            cacheContext,
            logger,
            ct);

        var packages = allMetadata.ToList();
        if (packages.Count == 0)
            throw new InvalidOperationException($"Package '{packageId}' not found on nuget.org");

        var versionRange = ParseVersionPattern(version);

        var matchingVersions = packages
            .Select(p => p.Identity.Version)
            .Where(v => versionRange.Satisfies(v))
            .OrderByDescending(v => v)
            .ToList();

        if (matchingVersions.Count == 0)
        {
            var displayVersion = string.IsNullOrWhiteSpace(version) ? "latest" : version;
            throw new InvalidOperationException(
                $"No version matching '{displayVersion}' found for package '{packageId}'");
        }

        return matchingVersions[0];
    }

    private async Task DownloadToGlobalCacheAsync(
        SourceRepository repository,
        string packageId,
        NuGetVersion version,
        SourceCacheContext cacheContext,
        global::NuGet.Common.ILogger logger,
        CancellationToken ct)
    {
        var downloadResource = await repository.GetResourceAsync<DownloadResource>(ct);
        var identity = new PackageIdentity(packageId, version);
        var globalPackagesPath = GetGlobalPackagesPath();

        DownloadResourceResult? result = null;
        try
        {
            result = await downloadResource.GetDownloadResourceResultAsync(
                identity,
                new PackageDownloadContext(cacheContext),
                globalPackagesPath,
                logger,
                ct);

            if (result.Status != DownloadResourceResultStatus.Available &&
                result.Status != DownloadResourceResultStatus.AvailableWithoutStream)
            {
                throw new InvalidOperationException(
                    $"Failed to download package '{packageId}' version '{version}': {result.Status}");
            }
        }
        finally
        {
            result?.Dispose();
        }
    }

    private string GetGlobalPackagesPath()
    {
        var dummyPath = _packageCache.GetPackagePath("_probe_", "0.0.0");
        return Path.GetDirectoryName(Path.GetDirectoryName(dummyPath))!;
    }

    [GeneratedRegex(@"^(\d+(?:\.\d+)?)\.\*$")]
    private static partial Regex WildcardPattern();

    public static IServiceCollection AddNuGetService(IServiceCollection services)
    {
        services.AddSingleton<INuGetService, NuGetService>();
        return services;
    }
}
