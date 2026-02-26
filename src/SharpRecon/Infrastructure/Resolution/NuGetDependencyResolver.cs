using Microsoft.Extensions.Logging;
using global::NuGet.Frameworks;
using global::NuGet.Packaging;
using global::NuGet.Packaging.Core;
using global::NuGet.Protocol;
using global::NuGet.Protocol.Core.Types;
using global::NuGet.Versioning;

namespace SharpRecon.Infrastructure.Resolution;

internal sealed class NuGetDependencyResolver
{
    private const string NuGetSourceUrl = "https://api.nuget.org/v3/index.json";
    private static readonly TimeSpan PerPackageTimeout = TimeSpan.FromSeconds(10);

    private readonly IPackageCache _packageCache;
    private readonly ILogger<NuGetDependencyResolver> _logger;
    private readonly int _maxDepth;

    public NuGetDependencyResolver(
        IPackageCache packageCache,
        ILogger<NuGetDependencyResolver> logger,
        int maxDepth = 6)
    {
        _packageCache = packageCache;
        _logger = logger;
        _maxDepth = maxDepth;
    }

    public async Task<DependencyResolutionResult> ResolveTransitiveDependenciesAsync(
        string packageId,
        string version,
        NuGetFramework targetFramework,
        CancellationToken cancellationToken)
    {
        var resolvedPackages = new Dictionary<string, NuGetVersion>(StringComparer.OrdinalIgnoreCase);
        var unresolvedDependencies = new List<string>();

        var repository = Repository.Factory.GetCoreV3(NuGetSourceUrl);
        var dependencyInfoResource = await repository.GetResourceAsync<DependencyInfoResource>(cancellationToken);
        var nugetLogger = global::NuGet.Common.NullLogger.Instance;
        var cacheContext = new SourceCacheContext();

        var rootIdentity = new PackageIdentity(packageId, NuGetVersion.Parse(version));
        resolvedPackages[packageId] = rootIdentity.Version;

        var queue = new Queue<(PackageIdentity Package, int Depth)>();
        queue.Enqueue((rootIdentity, 0));

        while (queue.Count > 0)
        {
            var (currentPackage, depth) = queue.Dequeue();

            if (depth >= _maxDepth)
                continue;

            SourcePackageDependencyInfo? packageInfo;
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(PerPackageTimeout);

                packageInfo = await dependencyInfoResource.ResolvePackage(
                    currentPackage,
                    targetFramework,
                    cacheContext,
                    nugetLogger,
                    timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Timeout resolving dependencies for {PackageId} {Version}",
                    currentPackage.Id, currentPackage.Version);
                unresolvedDependencies.Add($"{currentPackage.Id} {currentPackage.Version} (timeout)");
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve dependencies for {PackageId} {Version}",
                    currentPackage.Id, currentPackage.Version);
                unresolvedDependencies.Add($"{currentPackage.Id} {currentPackage.Version} (error: {ex.Message})");
                continue;
            }

            if (packageInfo is null)
            {
                if (!currentPackage.Equals(rootIdentity))
                    unresolvedDependencies.Add($"{currentPackage.Id} {currentPackage.Version} (not found)");
                continue;
            }

            foreach (var dependency in packageInfo.Dependencies)
            {
                if (resolvedPackages.ContainsKey(dependency.Id))
                    continue;

                var bestVersion = dependency.VersionRange.MinVersion ?? new NuGetVersion(0, 0, 0);
                resolvedPackages[dependency.Id] = bestVersion;

                queue.Enqueue((new PackageIdentity(dependency.Id, bestVersion), depth + 1));
            }
        }

        return new DependencyResolutionResult(resolvedPackages, unresolvedDependencies);
    }

    public async Task EnsurePackageDownloadedAsync(
        string packageId,
        NuGetVersion version,
        CancellationToken cancellationToken)
    {
        if (_packageCache.IsPackageCached(packageId, version.ToNormalizedString()))
            return;

        var repository = Repository.Factory.GetCoreV3(NuGetSourceUrl);
        var downloadResource = await repository.GetResourceAsync<DownloadResource>(cancellationToken);
        var nugetLogger = global::NuGet.Common.NullLogger.Instance;
        var cacheContext = new SourceCacheContext();

        var identity = new PackageIdentity(packageId, version);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(PerPackageTimeout);

        DownloadResourceResult? downloadResult = null;
        try
        {
            downloadResult = await downloadResource.GetDownloadResourceResultAsync(
                identity,
                new PackageDownloadContext(cacheContext),
                GetGlobalPackagesPath(),
                nugetLogger,
                timeoutCts.Token);

            if (downloadResult.Status == DownloadResourceResultStatus.Available ||
                downloadResult.Status == DownloadResourceResultStatus.AvailableWithoutStream)
            {
                _logger.LogDebug("Downloaded {PackageId} {Version} to global cache", packageId, version);
            }
            else
            {
                _logger.LogWarning("Could not download {PackageId} {Version}: {Status}",
                    packageId, version, downloadResult.Status);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download {PackageId} {Version}", packageId, version);
        }
        finally
        {
            downloadResult?.Dispose();
        }
    }

    private string GetGlobalPackagesPath()
    {
        var dummyPath = _packageCache.GetPackagePath("_probe_", "0.0.0");
        return Path.GetDirectoryName(Path.GetDirectoryName(dummyPath))!;
    }
}

internal sealed record DependencyResolutionResult(
    IReadOnlyDictionary<string, NuGetVersion> ResolvedPackages,
    IReadOnlyList<string> UnresolvedDependencies);
