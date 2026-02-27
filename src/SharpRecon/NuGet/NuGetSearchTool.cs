using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace SharpRecon.NuGet;

[McpServerToolType]
internal sealed class NuGetSearchTool
{
    [McpServerTool(Name = "nuget_search")]
    [Description("Searches NuGet.org for packages matching a query. Returns package IDs, latest versions, and descriptions. Use this to discover package IDs when you don't already know them, then call nuget_download to proceed.")]
    public static async Task<CallToolResult> SearchAsync(
        [Description("Search terms, e.g. 'json serializer' or 'Serilog' or 'Microsoft.Extensions'")] string query,
        INuGetService nuGetService,
        CancellationToken ct,
        [Description("Number of results to return (1-20, default 10)")] int? take = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query))
                throw new InvalidOperationException("Search query must not be empty");

            var actualTake = Math.Clamp(take ?? 10, 1, 20);
            var results = await nuGetService.SearchAsync(query, actualTake, ct);

            if (results.Count == 0)
                return new CallToolResult
                {
                    Content = [new TextContentBlock { Text = $"No packages found matching '{query}'" }],
                };

            var sb = new StringBuilder();
            sb.AppendLine($"Results for \"{query}\" ({results.Count}):");
            sb.AppendLine();

            for (var i = 0; i < results.Count; i++)
            {
                var r = results[i];
                var verified = r.Verified ? " [verified]" : "";
                var downloads = NuGetService.FormatDownloadCount(r.TotalDownloads);
                sb.AppendLine($"{i + 1}. **{r.PackageId}** ({r.Version}){verified} — {downloads} downloads");
                if (!string.IsNullOrWhiteSpace(r.Description))
                    sb.AppendLine($"   {r.Description}");
                sb.AppendLine();
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
