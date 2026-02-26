using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SharpRecon.Infrastructure;

namespace SharpRecon.Inspection;

[McpServerToolType]
internal sealed class TypeSearchTool
{
    [McpServerTool(Name = "type_search")]
    [Description("Searches for public types by name across all assemblies in a package. Case-insensitive substring match. For a full listing of one assembly, use type_list instead.")]
    public static async Task<CallToolResult> SearchTypesAsync(
        [Description("NuGet package ID")] string packageId,
        [Description("Exact package version (from nuget_download)")] string version,
        [Description("Search string (case-insensitive substring match)")] string query,
        IAssemblyInspector inspector,
        IPackageCache packageCache,
        CancellationToken ct,
        [Description("TFM filter. Omit to auto-select highest.")] string? tfm = null,
        [Description("Scope search to a single assembly (without .dll)")] string? assemblyName = null,
        [Description("Maximum results (default 100)")] int? maxResults = null)
    {
        return await ToolHelper.ExecuteWithSemaphoreAsync(async () =>
        {
            var versionError = ToolHelper.ValidateExactVersion(version);
            if (versionError is not null) throw new InvalidOperationException(versionError);

            if (!packageCache.IsPackageCached(packageId, version))
                throw new InvalidOperationException(
                    $"Package '{packageId}' version '{version}' not found in cache. Call nuget_download first.");

            var effectiveMaxResults = maxResults is null or <= 0 ? 100 : maxResults.Value;

            var result = await inspector.SearchTypesAsync(packageId, version, tfm, assemblyName, query, effectiveMaxResults, ct);

            var sb = new StringBuilder();
            sb.AppendLine($"Package: {packageId} {version}");
            sb.AppendLine($"Query: \"{query}\"");
            sb.AppendLine();

            if (result.Matches.Count == 0)
            {
                sb.AppendLine("No matching types found.");
                return sb.ToString().TrimEnd();
            }

            foreach (var match in result.Matches)
            {
                sb.AppendLine($"- {match.FullName} ({match.Kind}) in {match.AssemblyName}");
            }

            if (result.TotalCount > result.Matches.Count)
            {
                sb.AppendLine();
                sb.AppendLine($"Showing {result.Matches.Count} of {result.TotalCount} matches. Narrow your query for more specific results.");
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
