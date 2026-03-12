using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SharpRecon.Infrastructure;

namespace SharpRecon.NuGet;

[McpServerToolType]
internal sealed class AssemblyListTool
{
    [McpServerTool(Name = "assembly_list")]
    [Description("Lists assemblies in a downloaded package or local load, grouped by TFM. Use to discover assembly names before calling type_list. Not required before type_search (which searches all assemblies automatically).")]
    public static CallToolResult ListAssemblies(
        [Description("Package or local load identifier (from nuget_download or local_load)")] string packageId,
        [Description("Version (from nuget_download or local_load)")] string version,
        IAssemblySource assemblySource,
        [Description("Target framework moniker to filter by, e.g. 'net8.0'. Omit to list all TFMs.")] string? tfm = null)
    {
        try
        {
            if (!packageId.StartsWith("local:", StringComparison.OrdinalIgnoreCase) && version.Contains('*'))
            {
                return new CallToolResult
                {
                    Content = [new TextContentBlock
                    {
                        Text = $"Wildcard versions are not supported for assembly_list. Use nuget_download first to resolve the exact version."
                    }],
                    IsError = true,
                };
            }

            if (!assemblySource.IsRegistered(packageId, version))
            {
                var hint = packageId.StartsWith("local:", StringComparison.OrdinalIgnoreCase)
                    ? "Call local_load first."
                    : "Call nuget_download first.";
                return new CallToolResult
                {
                    Content = [new TextContentBlock
                    {
                        Text = $"Source '{packageId}' version '{version}' not found. {hint}"
                    }],
                    IsError = true,
                };
            }

            var availableTfms = assemblySource.GetAvailableTfms(packageId, version);

            if (tfm is not null)
            {
                if (!availableTfms.Any(t => string.Equals(t, tfm, StringComparison.OrdinalIgnoreCase)))
                {
                    return new CallToolResult
                    {
                        Content = [new TextContentBlock
                        {
                            Text = $"TFM '{tfm}' not available. Available TFMs: {string.Join(", ", availableTfms)}"
                        }],
                        IsError = true,
                    };
                }

                var assemblies = assemblySource.GetAssembliesForTfm(packageId, version, tfm);
                var sb = new StringBuilder();
                sb.AppendLine($"Package: {packageId} {version}");
                sb.AppendLine($"TFM: {tfm}");
                sb.AppendLine();
                sb.AppendLine("Assemblies:");
                foreach (var asm in assemblies)
                {
                    sb.AppendLine($"  {asm}");
                }
                sb.AppendLine();
                sb.AppendLine($"Available TFMs: {string.Join(", ", availableTfms)}");

                return new CallToolResult
                {
                    Content = [new TextContentBlock { Text = sb.ToString().TrimEnd() }],
                };
            }

            var output = new StringBuilder();
            output.AppendLine($"Package: {packageId} {version}");
            output.AppendLine();

            foreach (var t in availableTfms)
            {
                var asmList = assemblySource.GetAssembliesForTfm(packageId, version, t);
                output.AppendLine($"{t}:");
                foreach (var asm in asmList)
                {
                    output.AppendLine($"  {asm}");
                }
            }

            output.AppendLine();
            output.AppendLine($"Available TFMs: {string.Join(", ", availableTfms)}");

            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = output.ToString().TrimEnd() }],
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
