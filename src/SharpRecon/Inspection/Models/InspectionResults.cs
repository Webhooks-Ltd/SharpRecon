namespace SharpRecon.Inspection.Models;

internal sealed record TypeListResult(
    IReadOnlyList<TypeListEntry> Types,
    IReadOnlyList<string> UnresolvedDependencies);

internal sealed record TypeListEntry(
    string FullName,
    string Namespace,
    string Kind);

internal sealed record TypeSearchResult(
    IReadOnlyList<TypeSearchEntry> Matches,
    int TotalCount,
    IReadOnlyList<string> UnresolvedDependencies);

internal sealed record TypeSearchEntry(
    string FullName,
    string Kind,
    string AssemblyName);

internal sealed record TypeDetailResult(
    string TypeDeclaration,
    string? Summary,
    IReadOnlyList<MemberGroup> MemberGroups,
    IReadOnlyList<string> UnresolvedDependencies);

internal sealed record MemberGroup(
    string Kind,
    IReadOnlyList<MemberSignature> Members);

internal sealed record MemberSignature(
    string Name,
    string Signature,
    string? Summary);

internal sealed record MemberDetailResult(
    string TypeName,
    string MemberName,
    IReadOnlyList<MemberOverload> Overloads,
    IReadOnlyList<string> UnresolvedDependencies);

internal sealed record MemberOverload(
    string Signature,
    string? Summary,
    IReadOnlyDictionary<string, string> Params,
    string? Returns,
    IReadOnlyList<XmlDocException> Exceptions,
    string? Remarks);
