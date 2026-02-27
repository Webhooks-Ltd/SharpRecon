using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SharpRecon.Infrastructure;

namespace SharpRecon.Inspection;

[McpServerToolType]
internal sealed class MemberDetailTool
{
    [McpServerTool(Name = "member_detail")]
    [Description("Returns all overload signatures and XML docs for a specific member of a type. Fast — no decompilation. Use after type_detail to drill into one member. For implementation source, use decompile_member.")]
    public static async Task<CallToolResult> GetMemberDetailAsync(
        [Description("NuGet package ID")] string packageId,
        [Description("Exact package version (from nuget_download)")] string version,
        [Description("Fully qualified type name, e.g. 'Newtonsoft.Json.JsonConvert'")] string typeName,
        [Description("Member name, e.g. 'SerializeObject'. Use '.ctor' for constructors.")] string memberName,
        IAssemblyInspector inspector,
        IPackageCache packageCache,
        CancellationToken ct,
        [Description("Fully qualified CLR parameter types for overload filtering, e.g. ['System.Object', 'System.String']. Use CLR names, not C# aliases (string->System.String, int->System.Int32, bool->System.Boolean, object->System.Object).")] string[]? parameterTypes = null,
        [Description("TFM filter. Omit to auto-select highest.")] string? tfm = null,
        [Description("Assembly name hint (without .dll), e.g. from type_search results. Omit to search all assemblies (slower).")] string? assemblyName = null)
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

            var result = await inspector.GetMemberDetailAsync(packageId, version, tfm, assemblyName, typeName, memberName, parameterTypes, ct);

            var sb = new StringBuilder();
            sb.AppendLine($"Type: {result.TypeName}");
            sb.AppendLine($"Member: {result.MemberName}");
            sb.AppendLine($"Overloads: {result.Overloads.Count}");

            foreach (var overload in result.Overloads)
            {
                sb.AppendLine();
                sb.AppendLine($"```csharp");
                sb.AppendLine(overload.Signature);
                sb.AppendLine($"```");

                if (overload.Summary is not null)
                    sb.AppendLine(overload.Summary);

                if (overload.Params.Count > 0)
                {
                    sb.AppendLine("**Parameters:**");
                    foreach (var (name, desc) in overload.Params)
                        sb.AppendLine($"- `{name}`: {desc}");
                }

                if (overload.Returns is not null)
                    sb.AppendLine($"**Returns:** {overload.Returns}");

                if (overload.Exceptions.Count > 0)
                {
                    sb.AppendLine("**Exceptions:**");
                    foreach (var ex in overload.Exceptions)
                        sb.AppendLine($"- `{ex.Type}`: {ex.Description}");
                }

                if (overload.Remarks is not null)
                {
                    sb.AppendLine("**Remarks:**");
                    sb.AppendLine(overload.Remarks);
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
