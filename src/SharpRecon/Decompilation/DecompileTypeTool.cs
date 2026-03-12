using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SharpRecon.Infrastructure;
using SharpRecon.Inspection;

namespace SharpRecon.Decompilation;

[McpServerToolType]
internal sealed class DecompileTypeTool
{
    private const int DefaultMaxLength = 50000;

    [McpServerTool(Name = "decompile_type")]
    [Description("Decompiles a type to full C# source code. Use when you need implementation details, not just signatures. For signatures and XML docs only, use type_detail instead.")]
    public static async Task<CallToolResult> DecompileTypeAsync(
        [Description("Package or local load identifier (from nuget_download or local_load)")] string packageId,
        [Description("Version (from nuget_download or local_load)")] string version,
        [Description("Fully qualified type name, e.g. 'Newtonsoft.Json.JsonConvert'")] string typeName,
        AssemblyDecompiler decompiler,
        IAssemblySource assemblySource,
        LocalAssemblyRegistry localRegistry,
        CancellationToken ct,
        [Description("TFM filter. Omit to auto-select highest.")] string? tfm = null,
        [Description("Max output characters (default 50000). Truncates if exceeded.")] int? maxLength = null)
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

            var effectiveMaxLength = maxLength is null or <= 0 ? DefaultMaxLength : maxLength.Value;

            var resolvedTfm = tfm ?? TfmSelector.SelectBest(assemblySource, packageId, version);
            var result = await decompiler.DecompileTypeAsync(packageId, version, resolvedTfm, typeName, ct);

            var sb = new StringBuilder();
            var source = result.Source;
            if (source.Length > effectiveMaxLength)
            {
                sb.AppendLine(source[..effectiveMaxLength]);
                sb.AppendLine($"// ... output truncated at {effectiveMaxLength} characters");
            }
            else
            {
                sb.Append(source);
            }

            if (packageId.StartsWith("local:", StringComparison.OrdinalIgnoreCase))
            {
                var reg = localRegistry.TryGet(packageId);
                if (reg is { HasMixedModeAssemblies: true })
                {
                    sb.AppendLine();
                    sb.AppendLine();
                    sb.AppendLine("// Warning: This is a mixed-mode assembly. Native method bodies cannot be decompiled.");
                }
            }

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
