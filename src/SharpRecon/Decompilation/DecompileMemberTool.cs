using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SharpRecon.Infrastructure;
using SharpRecon.Inspection;

namespace SharpRecon.Decompilation;

[McpServerToolType]
internal sealed class DecompileMemberTool
{
    [McpServerTool(Name = "decompile_member")]
    [Description("Decompiles a single member to C# source code. Use parameterTypes to disambiguate overloads. For all overload signatures without source, use member_detail instead.")]
    public static async Task<CallToolResult> DecompileMemberAsync(
        [Description("NuGet package ID")] string packageId,
        [Description("Exact package version (from nuget_download)")] string version,
        [Description("Fully qualified type name, e.g. 'Newtonsoft.Json.JsonConvert'")] string typeName,
        [Description("Member name, e.g. 'SerializeObject'. Use '.ctor' for constructors.")] string memberName,
        AssemblyDecompiler decompiler,
        IPackageCache packageCache,
        CancellationToken ct,
        [Description("Fully qualified CLR parameter types for overload disambiguation, e.g. ['System.String', 'System.Int32']. Use CLR names, not C# aliases.")] string[]? parameterTypes = null,
        [Description("TFM filter. Omit to auto-select highest.")] string? tfm = null)
    {
        return await ToolHelper.ExecuteWithSemaphoreAsync(async () =>
        {
            var versionError = ToolHelper.ValidateExactVersion(version);
            if (versionError is not null) throw new InvalidOperationException(versionError);

            var paramError = ToolHelper.ValidateParameterTypes(parameterTypes);
            if (paramError is not null) throw new InvalidOperationException(paramError);

            if (!packageCache.IsPackageCached(packageId, version))
                throw new InvalidOperationException(
                    $"Package '{packageId}' version '{version}' not found in cache. Call nuget_download first.");

            var resolvedTfm = tfm ?? TfmSelector.SelectBest(packageCache, packageId, version);
            var result = await decompiler.DecompileMemberAsync(
                packageId, version, resolvedTfm, typeName, memberName, parameterTypes, ct);

            var sb = new StringBuilder();
            sb.Append(result.Source);

            if (result.UnresolvedDependencies.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine();
                sb.AppendLine("// Unresolved dependencies:");
                foreach (var dep in result.UnresolvedDependencies)
                    sb.AppendLine($"//   {dep}");
            }

            return sb.ToString().TrimEnd();
        }, ct);
    }
}
