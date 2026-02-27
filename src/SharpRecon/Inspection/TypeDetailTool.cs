using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SharpRecon.Infrastructure;

namespace SharpRecon.Inspection;

[McpServerToolType]
internal sealed class TypeDetailTool
{
    [McpServerTool(Name = "type_detail")]
    [Description("Returns the full C# type declaration: XML doc summary, base types, and all public member signatures grouped by kind. Fast — no decompilation. Use after type_list/type_search to inspect a type. For implementation source, use decompile_type.")]
    public static async Task<CallToolResult> GetTypeDetailAsync(
        [Description("NuGet package ID")] string packageId,
        [Description("Exact package version (from nuget_download)")] string version,
        [Description("Fully qualified type name, e.g. 'Newtonsoft.Json.JsonConvert'")] string typeName,
        IAssemblyInspector inspector,
        IPackageCache packageCache,
        CancellationToken ct,
        [Description("TFM filter. Omit to auto-select highest.")] string? tfm = null,
        [Description("Assembly name hint (without .dll), e.g. from type_search results. Omit to search all assemblies (slower).")] string? assemblyName = null)
    {
        return await ToolHelper.ExecuteWithSemaphoreAsync(async () =>
        {
            var versionError = ToolHelper.ValidateExactVersion(version);
            if (versionError is not null) throw new InvalidOperationException(versionError);

            if (!packageCache.IsPackageCached(packageId, version))
                throw new InvalidOperationException(
                    $"Package '{packageId}' version '{version}' not found in cache. Call nuget_download first.");

            var result = await inspector.GetTypeDetailAsync(packageId, version, tfm, assemblyName, typeName, ct);

            var sb = new StringBuilder();
            sb.AppendLine($"```csharp");
            sb.AppendLine(result.TypeDeclaration);
            sb.AppendLine($"```");

            if (result.Summary is not null)
            {
                sb.AppendLine();
                sb.AppendLine(result.Summary);
            }

            foreach (var group in result.MemberGroups)
            {
                sb.AppendLine();
                sb.AppendLine($"### {group.Kind}");
                foreach (var member in group.Members)
                {
                    sb.AppendLine($"- `{member.Signature}`");
                    if (member.Summary is not null)
                        sb.AppendLine($"  {member.Summary}");
                }
            }

            if (result.UnresolvedDependencies.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("**Unresolved dependencies:**");
                foreach (var dep in result.UnresolvedDependencies)
                    sb.AppendLine($"- {dep}");
            }

            return sb.ToString().TrimEnd();
        }, ct);
    }
}
