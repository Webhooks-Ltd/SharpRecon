using System.ComponentModel;
using System.Text;
using global::NuGet.Versioning;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace SharpRecon.NuGet;

[McpServerToolType]
internal sealed class NuGetDownloadTool
{
    [McpServerTool(Name = "nuget_download")]
    [Description("Downloads a NuGet package to the local cache. Call this first — all other tools require the exact version from this response. If you don't know the exact package ID, call nuget_search first. Returns resolved version, cache path, available TFMs, and package health (deprecation, vulnerabilities, publish date).")]
    public static async Task<CallToolResult> DownloadAsync(
        [Description("NuGet package ID, e.g. 'Newtonsoft.Json'")] string packageId,
        INuGetService nuGetService,
        CancellationToken ct,
        [Description("Exact version, wildcard pattern ('2.*', '2.1.*'), or omit for latest stable")] string? version = null)
    {
        try
        {
            var result = await nuGetService.DownloadPackageAsync(packageId, version, ct);

            PackageHealthInfo? health = null;
            try
            {
                health = await nuGetService.GetPackageHealthAsync(
                    packageId, NuGetVersion.Parse(result.ResolvedVersion), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
            }

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

            sb.AppendLine();
            FormatHealth(health, sb);

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

    internal static void FormatHealth(PackageHealthInfo? health, StringBuilder sb)
    {
        if (health is null)
        {
            sb.AppendLine("Health: unavailable (metadata query failed)");
            return;
        }

        sb.AppendLine("Health:");

        var published = health.Published.HasValue
            ? health.Published.Value.UtcDateTime.ToString("yyyy-MM-dd")
            : "unknown";
        sb.AppendLine($"  Published: {published}");

        if (health.Deprecation is not null)
        {
            var reasons = health.Deprecation.Reasons.Count > 0
                ? string.Join(", ", health.Deprecation.Reasons)
                : "Deprecated";
            var message = health.Deprecation.Message is not null
                ? $": \"{health.Deprecation.Message}\""
                : "";
            sb.AppendLine($"  DEPRECATED ({reasons}){message}");

            if (health.Deprecation.AlternatePackage is not null)
            {
                var alt = health.Deprecation.AlternatePackage;
                sb.AppendLine($"    Alternate: {alt.PackageId} {alt.VersionRange}");
            }
        }
        else
        {
            sb.AppendLine("  No deprecation notices.");
        }

        if (health.Vulnerabilities.Count > 0)
        {
            sb.AppendLine("  Vulnerabilities:");
            foreach (var v in health.Vulnerabilities)
            {
                sb.AppendLine($"    - {v.Severity.ToUpperInvariant()}: {v.AdvisoryUrl}");
            }
        }
        else
        {
            sb.AppendLine("  No known vulnerabilities.");
        }
    }
}
