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
    [Description("Lists assemblies in a cached NuGet package, grouped by TFM. Use to discover assembly names before calling type_list or type_search.")]
    public static CallToolResult ListAssemblies(
        [Description("NuGet package ID")] string packageId,
        [Description("Exact package version (from nuget_download)")] string version,
        IPackageCache packageCache,
        [Description("Target framework moniker to filter by, e.g. 'net8.0'. Omit to list all TFMs.")] string? tfm = null)
    {
        try
        {
            if (version.Contains('*'))
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

            if (!packageCache.IsPackageCached(packageId, version))
            {
                return new CallToolResult
                {
                    Content = [new TextContentBlock
                    {
                        Text = $"Package '{packageId}' version '{version}' not found in cache. Call nuget_download first."
                    }],
                    IsError = true,
                };
            }

            var availableTfms = packageCache.GetAvailableTfms(packageId, version);

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

                var assemblies = packageCache.GetAssembliesForTfm(packageId, version, tfm);
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
                var asmList = packageCache.GetAssembliesForTfm(packageId, version, t);
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
