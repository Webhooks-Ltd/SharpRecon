namespace SharpRecon.Infrastructure;

internal sealed class LocalAssemblySource : IAssemblySource
{
    private readonly LocalAssemblyRegistry _registry;

    public LocalAssemblySource(LocalAssemblyRegistry registry)
    {
        _registry = registry;
    }

    public bool IsRegistered(string sourceId, string version) =>
        _registry.IsRegistered(sourceId);

    public IReadOnlyList<string> GetAvailableTfms(string sourceId, string version)
    {
        var reg = _registry.TryGet(sourceId);
        return reg is not null ? [reg.InferredTfm] : [];
    }

    public IReadOnlyList<string> GetAssembliesForTfm(string sourceId, string version, string tfm)
    {
        var reg = _registry.TryGet(sourceId);
        if (reg is null)
            return [];

        return reg.AssemblyPaths
            .Select(p => Path.GetFileNameWithoutExtension(p))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string? GetAssemblyPath(string sourceId, string version, string tfm, string assemblyName, bool preferRef)
    {
        var reg = _registry.TryGet(sourceId);
        if (reg is null)
            return null;

        return reg.AssemblyPaths
            .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p)
                .Equals(assemblyName, StringComparison.OrdinalIgnoreCase));
    }

    public string? GetXmlDocPath(string sourceId, string version, string tfm, string assemblyName)
    {
        var reg = _registry.TryGet(sourceId);
        if (reg is null)
            return null;

        return reg.XmlDocPaths.TryGetValue(assemblyName, out var xmlPath) ? xmlPath : null;
    }

    public string? GetDepsJsonPath(string sourceId, string version)
    {
        var reg = _registry.TryGet(sourceId);
        return reg?.DepsJsonPath;
    }

}
