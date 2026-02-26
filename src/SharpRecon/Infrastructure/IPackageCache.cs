namespace SharpRecon.Infrastructure;

public interface IPackageCache
{
    string GetPackagePath(string packageId, string version);
    bool IsPackageCached(string packageId, string version);
    string? GetAssemblyPath(string packageId, string version, string tfm, string assemblyName, bool preferRef);
    IReadOnlyList<string> GetAvailableTfms(string packageId, string version);
    IReadOnlyList<string> GetAssembliesForTfm(string packageId, string version, string tfm);
}
