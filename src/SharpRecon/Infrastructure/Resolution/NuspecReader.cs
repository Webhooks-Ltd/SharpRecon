using global::NuGet.Frameworks;
using global::NuGet.Versioning;

namespace SharpRecon.Infrastructure.Resolution;

internal sealed class NuspecReader
{
    public IReadOnlyList<(string PackageId, VersionRange VersionRange)> GetDependencies(string packagePath, NuGetFramework targetFramework)
    {
        var nuspecPath = FindNuspecFile(packagePath);
        if (nuspecPath is null)
            return [];

        try
        {
            using var stream = File.OpenRead(nuspecPath);
            var reader = new global::NuGet.Packaging.NuspecReader(stream);
            var dependencyGroups = reader.GetDependencyGroups().ToList();

            if (dependencyGroups.Count == 0)
                return [];

            var reducer = new FrameworkReducer();
            var frameworks = dependencyGroups.Select(g => g.TargetFramework).ToList();
            var nearest = reducer.GetNearest(targetFramework, frameworks);

            if (nearest is null)
                return [];

            var group = dependencyGroups.First(g => g.TargetFramework.Equals(nearest));

            return group.Packages
                .Select(p => (p.Id, p.VersionRange))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static string? FindNuspecFile(string packagePath)
    {
        if (!Directory.Exists(packagePath))
            return null;

        var nuspecFiles = Directory.GetFiles(packagePath, "*.nuspec");
        return nuspecFiles.Length == 1 ? nuspecFiles[0] : null;
    }
}
