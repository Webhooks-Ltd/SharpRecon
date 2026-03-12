using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using global::NuGet.Frameworks;
using global::NuGet.Versioning;

namespace SharpRecon.Infrastructure.Resolution;

internal sealed record AssemblyResolutionResult(
    string PrimaryAssemblyPath,
    IReadOnlyList<string> AllAssemblyPaths,
    IReadOnlyList<string> UnresolvedDependencies);

internal sealed class AssemblyPathResolver
{
    private readonly IPackageCache _packageCache;
    private readonly FrameworkAssemblyResolver _frameworkResolver;
    private readonly GlobalCacheAssemblyResolver _cacheResolver;
    private readonly NuGetDependencyResolver _dependencyResolver;
    private readonly ILogger<AssemblyPathResolver> _logger;

    private readonly ConcurrentDictionary<string, AssemblyResolutionResult> _cache = new(StringComparer.OrdinalIgnoreCase);

    public AssemblyPathResolver(
        IPackageCache packageCache,
        FrameworkAssemblyResolver frameworkResolver,
        GlobalCacheAssemblyResolver cacheResolver,
        NuGetDependencyResolver dependencyResolver,
        ILogger<AssemblyPathResolver> logger)
    {
        _packageCache = packageCache;
        _frameworkResolver = frameworkResolver;
        _cacheResolver = cacheResolver;
        _dependencyResolver = dependencyResolver;
        _logger = logger;
    }

    public async Task<AssemblyResolutionResult> ResolveAsync(
        string packageId,
        string version,
        string tfm,
        string assemblyName,
        bool preferRef,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"{packageId}/{version}/{tfm}";

        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var result = await ResolveInternalAsync(packageId, version, tfm, assemblyName, preferRef, cancellationToken);
        _cache.TryAdd(cacheKey, result);
        return result;
    }

    public void InvalidateLocalCache(string sourceId)
    {
        var keysToRemove = _cache.Keys
            .Where(k => k.StartsWith($"{sourceId}/", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var key in keysToRemove)
            _cache.TryRemove(key, out _);
    }

    public async Task<AssemblyResolutionResult> ResolveLocalAsync(
        string sourceId, string version, string tfm, string assemblyName, IAssemblySource assemblySource, CancellationToken ct)
    {
        var cacheKey = $"{sourceId}/{assemblyName}";
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var primaryPath = assemblySource.GetAssemblyPath(sourceId, version, tfm, assemblyName, preferRef: false);
        if (primaryPath is null)
        {
            return new AssemblyResolutionResult(
                string.Empty, [], [$"Assembly '{assemblyName}' not found in local source '{sourceId}'"]);
        }

        var allPaths = new List<string>();
        var directory = Path.GetDirectoryName(primaryPath)!;

        var depsJsonPath = assemblySource.GetDepsJsonPath(sourceId, version);
        if (depsJsonPath is not null)
        {
            var depsPaths = DepsJsonParser.ResolveAssemblyPaths(depsJsonPath, directory);
            allPaths.AddRange(depsPaths);
        }

        var frameworkPaths = _frameworkResolver.GetFrameworkAssemblyPaths(tfm);
        allPaths.AddRange(frameworkPaths);

        var siblingDlls = Directory.EnumerateFiles(directory, "*.dll")
            .Where(f => !string.Equals(f, primaryPath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        allPaths.AddRange(siblingDlls);

        allPaths.Add(primaryPath);

        var distinctPaths = allPaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new AssemblyResolutionResult(primaryPath, distinctPaths, []);
        _cache.TryAdd(cacheKey, result);
        return result;
    }

    private async Task<AssemblyResolutionResult> ResolveInternalAsync(
        string packageId,
        string version,
        string tfm,
        string assemblyName,
        bool preferRef,
        CancellationToken cancellationToken)
    {
        var allPaths = new List<string>();
        var unresolvedDependencies = new List<string>();

        var primaryPath = _packageCache.GetAssemblyPath(packageId, version, tfm, assemblyName, preferRef);
        if (primaryPath is null)
        {
            return new AssemblyResolutionResult(
                string.Empty,
                [],
                [$"Primary assembly '{assemblyName}' not found in {packageId} {version} ({tfm})"]);
        }

        var frameworkPaths = _frameworkResolver.GetFrameworkAssemblyPaths(tfm);
        allPaths.AddRange(frameworkPaths);

        var targetFramework = NuGetFramework.ParseFolder(tfm);
        var declaredDependencies = _cacheResolver.GetDeclaredDependencies(packageId, version, targetFramework);

        var tier3Needed = new List<(string PackageId, VersionRange VersionRange)>();

        foreach (var (depId, versionRange) in declaredDependencies)
        {
            var depAssemblies = _cacheResolver.ResolveAllAssembliesFromCache(depId, versionRange, targetFramework);
            if (depAssemblies.Count > 0)
            {
                allPaths.AddRange(depAssemblies);
            }
            else
            {
                tier3Needed.Add((depId, versionRange));
            }
        }

        if (tier3Needed.Count > 0)
        {
            try
            {
                var resolution = await _dependencyResolver.ResolveTransitiveDependenciesAsync(
                    packageId, version, targetFramework, cancellationToken);

                foreach (var (resolvedId, resolvedVersion) in resolution.ResolvedPackages)
                {
                    if (string.Equals(resolvedId, packageId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        await _dependencyResolver.EnsurePackageDownloadedAsync(resolvedId, resolvedVersion, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to download dependency {PackageId} {Version}", resolvedId, resolvedVersion);
                        unresolvedDependencies.Add($"{resolvedId} {resolvedVersion.ToNormalizedString()}");
                        continue;
                    }

                    var versionRange = new VersionRange(resolvedVersion, includeMinVersion: true, resolvedVersion, includeMaxVersion: true);
                    var assemblies = _cacheResolver.ResolveAllAssembliesFromCache(resolvedId, versionRange, targetFramework);
                    if (assemblies.Count > 0)
                    {
                        allPaths.AddRange(assemblies);
                    }
                }

                foreach (var unresolved in resolution.UnresolvedDependencies)
                {
                    unresolvedDependencies.Add(unresolved);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "NuGet dependency resolution failed for {PackageId} {Version}", packageId, version);
                foreach (var (depId, _) in tier3Needed)
                {
                    unresolvedDependencies.Add(depId);
                }
            }
        }

        allPaths.Add(primaryPath);

        var distinctPaths = allPaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AssemblyResolutionResult(primaryPath, distinctPaths, unresolvedDependencies);
    }
}
