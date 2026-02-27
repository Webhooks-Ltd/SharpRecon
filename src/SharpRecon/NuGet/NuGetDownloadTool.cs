using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace SharpRecon.NuGet;

[McpServerToolType]
internal sealed class NuGetDownloadTool
{
    [McpServerTool(Name = "nuget_download")]
    [Description("Downloads a NuGet package to the local cache. Returns resolved version, cache path, and available TFMs. Call this before any inspection or decompilation tool — they require the exact version from this response. If you don't know the exact package ID, call nuget_search first.")]
    public static async Task<CallToolResult> DownloadAsync(
        [Description("NuGet package ID, e.g. 'Newtonsoft.Json'")] string packageId,
        INuGetService nuGetService,
        CancellationToken ct,
        [Description("Exact version, wildcard pattern ('2.*', '2.1.*'), or omit for latest stable")] string? version = null)
    {
        try
        {
            var result = await nuGetService.DownloadPackageAsync(packageId, version, ct);

            var sb = new StringBuilder();
            sb.AppendLine($"Package: {result.PackageId}");
            sb.AppendLine($"Version: {result.ResolvedVersion}");
            sb.AppendLine($"Cache path: {result.CachePath}");
            sb.AppendLine();
            sb.AppendLine("Available TFMs:");
            foreach (var tfm in result.AvailableTfms)
            {
                sb.AppendLine($"  {tfm}");
            }

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
