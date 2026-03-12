using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace SharpRecon.Infrastructure;

[McpServerToolType]
internal sealed class LocalLoadTool
{
    [McpServerTool(Name = "local_load")]
    [Description("Loads a local .NET assembly (.dll/.exe) or directory of assemblies for inspection and decompilation. Returns synthetic identifiers to use with other tools. For NuGet packages, use nuget_download instead.")]
    public static CallToolResult LoadLocal(
        [Description("Path to a .dll, .exe, or directory containing .NET assemblies")] string path,
        LocalAssemblyRegistry registry)
    {
        try
        {
            var result = registry.Register(path);

            var sb = new StringBuilder();
            sb.AppendLine($"Source: {result.SyntheticId}");
            sb.AppendLine($"Version: {result.Version}");
            sb.AppendLine($"Path: {result.Path}");
            sb.AppendLine($"Inferred TFM: {result.InferredTfm}");

            if (result.NativeSkipCount > 0)
                sb.AppendLine($"Assemblies loaded: {result.AssemblyCount} ({result.NativeSkipCount} native DLLs skipped)");
            else
                sb.AppendLine($"Assemblies loaded: {result.AssemblyCount}");

            sb.AppendLine();
            sb.AppendLine($"Next: use assembly_list, type_list, type_search, or decompile_type with packageId=\"{result.SyntheticId}\" version=\"local\"");

            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = sb.ToString().TrimEnd() }],
            };
        }
        catch (Exception ex)
        {
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = ex.Message }],
                IsError = true,
            };
        }
    }
}
