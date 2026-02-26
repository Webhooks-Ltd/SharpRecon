using SharpRecon.Inspection.Models;

namespace SharpRecon.Inspection;

internal interface IAssemblyInspector
{
    Task<TypeListResult> GetTypesAsync(string packageId, string version, string? tfm, string assemblyName, string? ns, CancellationToken ct);
    Task<TypeSearchResult> SearchTypesAsync(string packageId, string version, string? tfm, string? assemblyName, string query, int maxResults, CancellationToken ct);
    Task<TypeDetailResult> GetTypeDetailAsync(string packageId, string version, string? tfm, string? assemblyName, string typeName, CancellationToken ct);
    Task<MemberDetailResult> GetMemberDetailAsync(string packageId, string version, string? tfm, string? assemblyName, string typeName, string memberName, string[]? parameterTypes, CancellationToken ct);
}
