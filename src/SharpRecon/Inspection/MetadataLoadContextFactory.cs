using System.Reflection;

namespace SharpRecon.Inspection;

internal static class MetadataLoadContextFactory
{
    public static MetadataLoadContext Create(IReadOnlyList<string> assemblyPaths)
    {
        var resolver = new PathAssemblyResolver(assemblyPaths);
        var coreAssemblyName = FindCoreAssembly(assemblyPaths);
        return new MetadataLoadContext(resolver, coreAssemblyName);
    }

    private static string FindCoreAssembly(IReadOnlyList<string> assemblyPaths)
    {
        foreach (var path in assemblyPaths)
        {
            var fileName = Path.GetFileNameWithoutExtension(path);
            if (string.Equals(fileName, "System.Runtime", StringComparison.OrdinalIgnoreCase))
                return "System.Runtime";
        }

        foreach (var path in assemblyPaths)
        {
            var fileName = Path.GetFileNameWithoutExtension(path);
            if (string.Equals(fileName, "mscorlib", StringComparison.OrdinalIgnoreCase))
                return "mscorlib";
        }

        return "System.Runtime";
    }
}
