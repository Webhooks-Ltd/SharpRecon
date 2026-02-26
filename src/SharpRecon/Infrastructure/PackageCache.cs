using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;

namespace SharpRecon.Infrastructure;

public sealed class PackageCache : IPackageCache
{
    private readonly string _globalPackagesPath;

    public PackageCache()
        : this(ResolveGlobalPackagesPath())
    {
    }

    internal PackageCache(string globalPackagesPath)
    {
        _globalPackagesPath = globalPackagesPath;
    }

    public string GetPackagePath(string packageId, string version)
    {
        return Path.Combine(_globalPackagesPath, packageId.ToLowerInvariant(), version.ToLowerInvariant());
    }

    public bool IsPackageCached(string packageId, string version)
    {
        return Directory.Exists(GetPackagePath(packageId, version));
    }

    public string? GetAssemblyPath(string packageId, string version, string tfm, string assemblyName, bool preferRef)
    {
        var packagePath = GetPackagePath(packageId, version);
        var dllFileName = assemblyName + ".dll";

        if (preferRef)
        {
            var refPath = FindAssemblyInDirectory(Path.Combine(packagePath, "ref", tfm), dllFileName);
            if (refPath is not null)
                return refPath;
        }

        var libDir = Path.Combine(packagePath, "lib", tfm);
        var libPath = FindAssemblyInDirectory(libDir, dllFileName);
        if (libPath is not null)
            return libPath;

        if (IsEmptyOrMarkerOnly(libDir))
        {
            var runtimePath = FindInRuntimesDirectory(packagePath, tfm, dllFileName);
            if (runtimePath is not null)
                return runtimePath;
        }

        return null;
    }

    public IReadOnlyList<string> GetAvailableTfms(string packageId, string version)
    {
        var packagePath = GetPackagePath(packageId, version);
        var tfms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        CollectTfmsFromDirectory(Path.Combine(packagePath, "lib"), tfms);
        CollectTfmsFromDirectory(Path.Combine(packagePath, "ref"), tfms);

        return tfms.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<string> GetAssembliesForTfm(string packageId, string version, string tfm)
    {
        var packagePath = GetPackagePath(packageId, version);
        var libDir = Path.Combine(packagePath, "lib", tfm);

        var assemblies = GetDllNamesFromDirectory(libDir);
        if (assemblies.Count > 0)
            return assemblies;

        if (IsEmptyOrMarkerOnly(libDir))
        {
            var runtimeAssemblies = GetDllNamesFromRuntimesDirectory(packagePath, tfm);
            if (runtimeAssemblies.Count > 0)
                return runtimeAssemblies;
        }

        return [];
    }

    private static string? FindAssemblyInDirectory(string directory, string dllFileName)
    {
        if (!Directory.Exists(directory))
            return null;

        var match = Directory.EnumerateFiles(directory, "*.dll")
            .FirstOrDefault(f => string.Equals(Path.GetFileName(f), dllFileName, StringComparison.OrdinalIgnoreCase));

        return match;
    }

    private static string? FindInRuntimesDirectory(string packagePath, string tfm, string dllFileName)
    {
        var rid = RuntimeInformation.RuntimeIdentifier;
        var runtimeDir = Path.Combine(packagePath, "runtimes", rid, "lib", tfm);
        return FindAssemblyInDirectory(runtimeDir, dllFileName);
    }

    private static bool IsEmptyOrMarkerOnly(string directory)
    {
        if (!Directory.Exists(directory))
            return true;

        var files = Directory.EnumerateFiles(directory).ToList();
        return files.Count == 0 || files.All(f => Path.GetFileName(f) == "_._");
    }

    private static void CollectTfmsFromDirectory(string baseDir, HashSet<string> tfms)
    {
        if (!Directory.Exists(baseDir))
            return;

        foreach (var dir in Directory.EnumerateDirectories(baseDir))
        {
            tfms.Add(Path.GetFileName(dir));
        }
    }

    private static List<string> GetDllNamesFromDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            return [];

        return Directory.EnumerateFiles(directory, "*.dll")
            .Where(f => Path.GetFileName(f) != "_._")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> GetDllNamesFromRuntimesDirectory(string packagePath, string tfm)
    {
        var rid = RuntimeInformation.RuntimeIdentifier;
        var runtimeDir = Path.Combine(packagePath, "runtimes", rid, "lib", tfm);
        return GetDllNamesFromDirectory(runtimeDir);
    }

    private static string ResolveGlobalPackagesPath()
    {
        var envPath = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrWhiteSpace(envPath))
            return envPath;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages");
    }

    public static IServiceCollection AddPackageCache(IServiceCollection services)
    {
        services.AddSingleton<IPackageCache, PackageCache>();
        return services;
    }
}
