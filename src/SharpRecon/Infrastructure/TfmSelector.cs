using System.Globalization;

namespace SharpRecon.Infrastructure;

internal static class TfmSelector
{
    public static string SelectBest(IPackageCache packageCache, string packageId, string version)
    {
        var tfms = packageCache.GetAvailableTfms(packageId, version);
        if (tfms.Count == 0)
            throw new InvalidOperationException($"No TFMs available in {packageId} {version}.");

        return SelectBest(tfms);
    }

    public static string SelectBest(IReadOnlyList<string> tfms)
    {
        var netTfms = tfms
            .Where(t => t.StartsWith("net", StringComparison.OrdinalIgnoreCase)
                        && !t.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase)
                        && !t.StartsWith("netcoreapp", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(ParseVersion)
            .ToList();
        if (netTfms.Count > 0) return netTfms[0];

        var netstd = tfms
            .Where(t => t.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(ParseVersion)
            .ToList();
        if (netstd.Count > 0) return netstd[0];

        var netcore = tfms
            .Where(t => t.StartsWith("netcoreapp", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(ParseVersion)
            .ToList();
        if (netcore.Count > 0) return netcore[0];

        return tfms[0];
    }

    private static double ParseVersion(string tfm)
    {
        var versionPart = tfm;
        foreach (var prefix in (ReadOnlySpan<string>)["netcoreapp", "netstandard", "net"])
        {
            if (tfm.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                versionPart = tfm[prefix.Length..];
                break;
            }
        }

        if (double.TryParse(versionPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return v;
        return 0;
    }
}
