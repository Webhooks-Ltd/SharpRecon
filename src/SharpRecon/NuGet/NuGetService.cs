using System.Text.Json;
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
    private const string NuGetSearchUrl = "https://azuresearch-usnc.nuget.org/query";

    private readonly IPackageCache _packageCache;
    private readonly HttpClient _httpClient;

    public NuGetService(IPackageCache packageCache, HttpClient httpClient)
    {
        _packageCache = packageCache;
        _httpClient = httpClient;
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

    public async Task<IReadOnlyList<NuGetSearchResult>> SearchAsync(string query, int take, CancellationToken ct)
    {
        var url = $"{NuGetSearchUrl}?q={Uri.EscapeDataString(query)}&take={take}&semVerLevel=2.0.0";
        using var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var results = new List<NuGetSearchResult>();
        foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            var packageId = item.GetProperty("id").GetString()!;
            var version = item.GetProperty("version").GetString()!;
            var description = item.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "";
            var totalDownloads = item.TryGetProperty("totalDownloads", out var dl) ? dl.GetInt64() : 0;
            var verified = item.TryGetProperty("verified", out var v) && v.GetBoolean();

            if (description.Length > 200)
                description = description[..197] + "...";

            results.Add(new NuGetSearchResult(packageId, version, description, totalDownloads, verified));
        }

        return results;
    }

    public async Task<PackageHealthInfo> GetPackageHealthAsync(string packageId, NuGetVersion resolvedVersion, CancellationToken ct)
    {
        var repository = Repository.Factory.GetCoreV3(NuGetSourceUrl);
        var cacheContext = new SourceCacheContext();
        var nugetLogger = global::NuGet.Common.NullLogger.Instance;

        var metadataResource = await repository.GetResourceAsync<PackageMetadataResource>(ct);
        var identity = new PackageIdentity(packageId, resolvedVersion);
        var metadata = await metadataResource.GetMetadataAsync(identity, cacheContext, nugetLogger, ct);

        if (metadata is null)
            throw new InvalidOperationException($"No metadata found for '{packageId}' version '{resolvedVersion}'");

        var deprecation = await metadata.GetDeprecationMetadataAsync();
        DeprecationInfo? deprecationInfo = null;
        if (deprecation is not null)
        {
            AlternatePackageInfo? alternate = null;
            if (deprecation.AlternatePackage is not null)
            {
                alternate = new AlternatePackageInfo(
                    deprecation.AlternatePackage.PackageId,
                    deprecation.AlternatePackage.Range.ToString());
            }

            deprecationInfo = new DeprecationInfo(
                deprecation.Reasons?.ToList() ?? [],
                deprecation.Message,
                alternate);
        }

        var vulnerabilities = metadata.Vulnerabilities?
            .Select(v => new VulnerabilityInfo(MapSeverity(v.Severity), v.AdvisoryUrl))
            .ToList()
            ?? [];

        return new PackageHealthInfo(metadata.Published, deprecationInfo, vulnerabilities);
    }

    internal static string MapSeverity(int severity) => severity switch
    {
        0 => "Low",
        1 => "Moderate",
        2 => "High",
        3 => "Critical",
        _ => $"Unknown({severity})"
    };

    internal static string FormatDownloadCount(long count) => count switch
    {
        >= 1_000_000_000 => $"{count / 1_000_000_000.0:0.#}B",
        >= 1_000_000 => $"{count / 1_000_000.0:0.#}M",
        >= 1_000 => $"{count / 1_000.0:0.#}K",
        _ => count.ToString()
    };

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
        services.AddHttpClient<INuGetService, NuGetService>();
        return services;
    }
}
