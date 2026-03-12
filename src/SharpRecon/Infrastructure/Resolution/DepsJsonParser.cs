using System.Text.Json;

namespace SharpRecon.Infrastructure.Resolution;

internal static class DepsJsonParser
{
    public static IReadOnlyList<string> ResolveAssemblyPaths(string depsJsonPath, string assemblyDirectory)
    {
        try
        {
            if (!File.Exists(depsJsonPath))
                return [];

            var json = File.ReadAllText(depsJsonPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("runtimeTarget", out var runtimeTarget))
                return [];

            if (!runtimeTarget.TryGetProperty("name", out var runtimeTargetName))
                return [];

            var targetName = runtimeTargetName.GetString();
            if (string.IsNullOrEmpty(targetName))
                return [];

            if (!root.TryGetProperty("targets", out var targets))
                return [];

            if (!targets.TryGetProperty(targetName, out var targetEntries))
                return [];

            var libraries = root.TryGetProperty("libraries", out var libs) ? libs : default;

            var nugetPackagesPath = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

            var resolved = new List<string>();

            foreach (var entry in targetEntries.EnumerateObject())
            {
                if (!entry.Value.TryGetProperty("runtime", out var runtime))
                    continue;

                var libraryKey = entry.Name;
                string? libraryType = null;
                string? libraryPath = null;

                if (libraries.ValueKind == JsonValueKind.Object
                    && libraries.TryGetProperty(libraryKey, out var libEntry))
                {
                    if (libEntry.TryGetProperty("type", out var typeProp))
                        libraryType = typeProp.GetString();
                    if (libEntry.TryGetProperty("path", out var pathProp))
                        libraryPath = pathProp.GetString();
                }

                foreach (var runtimeEntry in runtime.EnumerateObject())
                {
                    var dllRelativePath = runtimeEntry.Name;
                    var dllFileName = Path.GetFileName(dllRelativePath);

                    var localPath = Path.Combine(assemblyDirectory, dllFileName);
                    if (File.Exists(localPath))
                    {
                        resolved.Add(localPath);
                        continue;
                    }

                    if (string.Equals(libraryType, "package", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrEmpty(libraryPath))
                    {
                        var cachePath = Path.GetFullPath(Path.Combine(nugetPackagesPath, libraryPath, dllRelativePath));
                        if (File.Exists(cachePath))
                        {
                            resolved.Add(cachePath);
                        }
                    }
                }
            }

            return resolved;
        }
        catch
        {
            return [];
        }
    }
}
