namespace SharpRecon.Infrastructure;

internal sealed class CompositeAssemblySource : IAssemblySource
{
    private readonly NuGetAssemblySource _nuget;
    private readonly LocalAssemblySource _local;

    public CompositeAssemblySource(NuGetAssemblySource nuget, LocalAssemblySource local)
    {
        _nuget = nuget;
        _local = local;
    }

    public bool IsRegistered(string sourceId, string version) =>
        GetSource(sourceId).IsRegistered(sourceId, version);

    public IReadOnlyList<string> GetAvailableTfms(string sourceId, string version) =>
        GetSource(sourceId).GetAvailableTfms(sourceId, version);

    public IReadOnlyList<string> GetAssembliesForTfm(string sourceId, string version, string tfm) =>
        GetSource(sourceId).GetAssembliesForTfm(sourceId, version, tfm);

    public string? GetAssemblyPath(string sourceId, string version, string tfm, string assemblyName, bool preferRef) =>
        GetSource(sourceId).GetAssemblyPath(sourceId, version, tfm, assemblyName, preferRef);

    public string? GetXmlDocPath(string sourceId, string version, string tfm, string assemblyName) =>
        GetSource(sourceId).GetXmlDocPath(sourceId, version, tfm, assemblyName);

    public string? GetDepsJsonPath(string sourceId, string version) =>
        GetSource(sourceId).GetDepsJsonPath(sourceId, version);

    private IAssemblySource GetSource(string sourceId) =>
        sourceId.StartsWith("local:", StringComparison.OrdinalIgnoreCase) ? _local : _nuget;
}
