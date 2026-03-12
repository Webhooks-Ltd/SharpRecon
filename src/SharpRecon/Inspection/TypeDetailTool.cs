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
        [Description("Package or local load identifier (from nuget_download or local_load)")] string packageId,
        [Description("Version (from nuget_download or local_load)")] string version,
        [Description("Fully qualified type name, e.g. 'Newtonsoft.Json.JsonConvert'")] string typeName,
        IAssemblyInspector inspector,
        IAssemblySource assemblySource,
        CancellationToken ct,
        [Description("TFM filter. Omit to auto-select highest.")] string? tfm = null,
        [Description("Assembly name hint (without .dll), e.g. from type_search results. Omit to search all assemblies (slower).")] string? assemblyName = null,
        [Description("Include inherited members from base types (default false).")] bool? includeInherited = null)
    {
        return await ToolHelper.ExecuteWithSemaphoreAsync(async () =>
        {
            if (!packageId.StartsWith("local:", StringComparison.OrdinalIgnoreCase))
            {
                var versionError = ToolHelper.ValidateExactVersion(version);
                if (versionError is not null) throw new InvalidOperationException(versionError);
            }

            if (!assemblySource.IsRegistered(packageId, version))
            {
                var hint = packageId.StartsWith("local:", StringComparison.OrdinalIgnoreCase)
                    ? "Call local_load first."
                    : "Call nuget_download first.";
                throw new InvalidOperationException(
                    $"Source '{packageId}' version '{version}' not found. {hint}");
            }

            var result = await inspector.GetTypeDetailAsync(packageId, version, tfm, assemblyName, typeName, includeInherited ?? false, ct);

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
