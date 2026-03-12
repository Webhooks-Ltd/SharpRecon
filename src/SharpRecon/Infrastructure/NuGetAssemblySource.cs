namespace SharpRecon.Infrastructure;

internal sealed class NuGetAssemblySource : IAssemblySource
{
    private readonly IPackageCache _packageCache;

    public NuGetAssemblySource(IPackageCache packageCache)
    {
        _packageCache = packageCache;
    }

    public bool IsRegistered(string sourceId, string version) =>
        _packageCache.IsPackageCached(sourceId, version);

    public IReadOnlyList<string> GetAvailableTfms(string sourceId, string version) =>
        _packageCache.GetAvailableTfms(sourceId, version);

    public IReadOnlyList<string> GetAssembliesForTfm(string sourceId, string version, string tfm) =>
        _packageCache.GetAssembliesForTfm(sourceId, version, tfm);

    public string? GetAssemblyPath(string sourceId, string version, string tfm, string assemblyName, bool preferRef) =>
        _packageCache.GetAssemblyPath(sourceId, version, tfm, assemblyName, preferRef);

    public string? GetXmlDocPath(string sourceId, string version, string tfm, string assemblyName)
    {
        var packagePath = _packageCache.GetPackagePath(sourceId, version);
        var xmlFileName = assemblyName + ".xml";

        var refXmlPath = Path.Combine(packagePath, "ref", tfm, xmlFileName);
        if (File.Exists(refXmlPath))
            return refXmlPath;

        var libXmlPath = Path.Combine(packagePath, "lib", tfm, xmlFileName);
        if (File.Exists(libXmlPath))
            return libXmlPath;

        return null;
    }

    public string? GetDepsJsonPath(string sourceId, string version) => null;
}
