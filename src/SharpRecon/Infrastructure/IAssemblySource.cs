namespace SharpRecon.Infrastructure;

internal interface IAssemblySource
{
    bool IsRegistered(string sourceId, string version);
    IReadOnlyList<string> GetAvailableTfms(string sourceId, string version);
    IReadOnlyList<string> GetAssembliesForTfm(string sourceId, string version, string tfm);
    string? GetAssemblyPath(string sourceId, string version, string tfm, string assemblyName, bool preferRef);
    string? GetXmlDocPath(string sourceId, string version, string tfm, string assemblyName);
    string? GetDepsJsonPath(string sourceId, string version);
}
