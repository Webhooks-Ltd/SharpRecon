using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SharpRecon.Infrastructure;

namespace SharpRecon.Inspection;

[McpServerToolType]
internal sealed class TypeListTool
{
    [McpServerTool(Name = "type_list")]
    [Description("Lists all public types in a specific assembly, grouped by namespace. Requires assemblyName (from assembly_list). Returns type full names and kinds (class, struct, enum, interface, delegate). For name-based search across all assemblies, use type_search instead.")]
    public static async Task<CallToolResult> ListTypesAsync(
        [Description("Package or local load identifier (from nuget_download or local_load)")] string packageId,
        [Description("Version (from nuget_download or local_load)")] string version,
        [Description("Assembly name without .dll extension")] string assemblyName,
        IAssemblyInspector inspector,
        IAssemblySource assemblySource,
        CancellationToken ct,
        [Description("TFM, e.g. 'net8.0'. Omit to auto-select highest.")] string? tfm = null,
        [Description("Filter to types in this namespace only")] string? ns = null)
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

            var result = await inspector.GetTypesAsync(packageId, version, tfm, assemblyName, ns, ct);

            var sb = new StringBuilder();
            sb.AppendLine($"Package: {packageId} {version}");
            sb.AppendLine($"Assembly: {assemblyName}");
            sb.AppendLine();

            var grouped = result.Types
                .GroupBy(t => t.Namespace)
                .OrderBy(g => g.Key, StringComparer.Ordinal);

            foreach (var group in grouped)
            {
                sb.AppendLine($"## {(group.Key.Length > 0 ? group.Key : "(global)")}");
                foreach (var t in group.OrderBy(x => x.FullName))
                {
                    sb.AppendLine($"- {t.FullName} ({t.Kind})");
                }
                sb.AppendLine();
            }

            if (result.UnresolvedDependencies.Count > 0)
            {
                sb.AppendLine("**Unresolved dependencies:**");
                foreach (var dep in result.UnresolvedDependencies)
                    sb.AppendLine($"- {dep}");
            }

            return sb.ToString().TrimEnd();
        }, ct);
    }
}
