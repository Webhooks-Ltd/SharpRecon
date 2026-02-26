using global::NuGet.Frameworks;
using global::NuGet.Versioning;

namespace SharpRecon.Infrastructure.Resolution;

internal sealed class GlobalCacheAssemblyResolver
{
    private readonly IPackageCache _packageCache;
    private readonly NuspecReader _nuspecReader;

    public GlobalCacheAssemblyResolver(IPackageCache packageCache, NuspecReader nuspecReader)
    {
        _packageCache = packageCache;
        _nuspecReader = nuspecReader;
    }

    public IReadOnlyList<(string PackageId, VersionRange VersionRange)> GetDeclaredDependencies(
        string packageId, string version, NuGetFramework targetFramework)
    {
        var packagePath = _packageCache.GetPackagePath(packageId, version);
        return _nuspecReader.GetDependencies(packagePath, targetFramework);
    }

    public string? ResolveAssemblyFromCache(
        string dependencyPackageId,
        VersionRange versionRange,
        NuGetFramework targetFramework)
    {
        var packageDir = Path.Combine(
            GetGlobalPackagesPath(),
            dependencyPackageId.ToLowerInvariant());

        if (!Directory.Exists(packageDir))
            return null;

        var bestVersion = FindBestMatchingVersion(packageDir, versionRange);
        if (bestVersion is null)
            return null;

        var packagePath = Path.Combine(packageDir, bestVersion.ToNormalizedString());
        return FindAssemblyForFramework(packagePath, targetFramework);
    }

    public IReadOnlyList<string> ResolveAllAssembliesFromCache(
        string dependencyPackageId,
        VersionRange versionRange,
        NuGetFramework targetFramework)
    {
        var packageDir = Path.Combine(
            GetGlobalPackagesPath(),
            dependencyPackageId.ToLowerInvariant());

        if (!Directory.Exists(packageDir))
            return [];

        var bestVersion = FindBestMatchingVersion(packageDir, versionRange);
        if (bestVersion is null)
            return [];

        var packagePath = Path.Combine(packageDir, bestVersion.ToNormalizedString());
        return FindAllAssembliesForFramework(packagePath, targetFramework);
    }

    private static NuGetVersion? FindBestMatchingVersion(string packageDir, VersionRange versionRange)
    {
        NuGetVersion? bestVersion = null;

        foreach (var versionDir in Directory.GetDirectories(packageDir))
        {
            var dirName = Path.GetFileName(versionDir);
            if (!NuGetVersion.TryParse(dirName, out var version))
                continue;

            if (!versionRange.Satisfies(version))
                continue;

            if (bestVersion is null || version < bestVersion)
                bestVersion = version;
        }

        return bestVersion;
    }

    private static string? FindAssemblyForFramework(string packagePath, NuGetFramework targetFramework)
    {
        var assemblies = FindAllAssembliesForFramework(packagePath, targetFramework);
        return assemblies.Count > 0 ? assemblies[0] : null;
    }

    private static IReadOnlyList<string> FindAllAssembliesForFramework(string packagePath, NuGetFramework targetFramework)
    {
        var libDir = Path.Combine(packagePath, "lib");
        if (!Directory.Exists(libDir))
            return [];

        var tfmDirs = Directory.GetDirectories(libDir);
        if (tfmDirs.Length == 0)
            return [];

        var frameworks = new List<(NuGetFramework Framework, string Path)>();
        foreach (var tfmDir in tfmDirs)
        {
            var tfmName = Path.GetFileName(tfmDir);
            var framework = NuGetFramework.ParseFolder(tfmName);
            if (framework.IsSpecificFramework)
                frameworks.Add((framework, tfmDir));
        }

        if (frameworks.Count == 0)
            return [];

        var reducer = new FrameworkReducer();
        var nearest = reducer.GetNearest(targetFramework, frameworks.Select(f => f.Framework));
        if (nearest is null)
            return [];

        var selectedDir = frameworks.First(f => f.Framework.Equals(nearest)).Path;
        var dlls = Directory.GetFiles(selectedDir, "*.dll")
            .Where(f => Path.GetFileName(f) != "_._")
            .ToList();

        return dlls;
    }

    private string GetGlobalPackagesPath()
    {
        var dummyPath = _packageCache.GetPackagePath("_probe_", "0.0.0");
        return Path.GetDirectoryName(Path.GetDirectoryName(dummyPath))!;
    }
}
