using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace SharpRecon.Infrastructure;

internal static class TfmInferrer
{
    public static string? InferTfm(string assemblyPath)
    {
        try
        {
            var coreAssemblyPath = typeof(object).Assembly.Location;
            var resolver = new PathAssemblyResolver([coreAssemblyPath, assemblyPath]);
            using var mlc = new MetadataLoadContext(resolver);
            var assembly = mlc.LoadFromAssemblyPath(assemblyPath);

            foreach (var attr in assembly.GetCustomAttributesData())
            {
                if (attr.AttributeType.FullName != "System.Runtime.Versioning.TargetFrameworkAttribute")
                    continue;
                if (attr.ConstructorArguments.Count == 0)
                    continue;

                var frameworkName = attr.ConstructorArguments[0].Value as string;
                if (frameworkName is not null)
                    return ParseFrameworkName(frameworkName);
            }
        }
        catch
        {
        }

        return null;
    }

    public static string? InferTfmFromRuntimeConfig(string runtimeConfigPath)
    {
        try
        {
            var json = File.ReadAllText(runtimeConfigPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("runtimeOptions", out var runtimeOptions))
                return null;

            if (runtimeOptions.TryGetProperty("framework", out var framework))
            {
                var tfm = ParseFrameworkFromJson(framework);
                if (tfm is not null)
                    return tfm;
            }

            if (runtimeOptions.TryGetProperty("frameworks", out var frameworks)
                && frameworks.ValueKind == JsonValueKind.Array)
            {
                foreach (var fw in frameworks.EnumerateArray())
                {
                    var tfm = ParseFrameworkFromJson(fw);
                    if (tfm is not null)
                        return tfm;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    public static string? InferTfmForDirectory(string directoryPath, IReadOnlyList<string> assemblyPaths)
    {
        var runtimeConfigs = Directory.EnumerateFiles(directoryPath, "*.runtimeconfig.json").ToList();
        if (runtimeConfigs.Count > 0)
        {
            var tfm = InferTfmFromRuntimeConfig(runtimeConfigs[0]);
            if (tfm is not null)
                return tfm;
        }

        var dirName = Path.GetFileName(directoryPath);

        var exePaths = assemblyPaths
            .Where(p => Path.GetExtension(p).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var exePath in exePaths)
        {
            var tfm = InferTfm(exePath);
            if (tfm is not null)
                return tfm;
        }

        var dllPaths = assemblyPaths
            .Where(p => Path.GetExtension(p).Equals(".dll", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var matchingDll = dllPaths
            .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p)
                .Equals(dirName, StringComparison.OrdinalIgnoreCase));
        if (matchingDll is not null)
        {
            var tfm = InferTfm(matchingDll);
            if (tfm is not null)
                return tfm;
        }

        var firstDll = dllPaths.OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        if (firstDll is not null)
        {
            var tfm = InferTfm(firstDll);
            if (tfm is not null)
                return tfm;
        }

        return null;
    }

    internal static string? ParseFrameworkName(string frameworkName)
    {
        var parts = frameworkName.Split(',');
        if (parts.Length < 2)
            return null;

        var identifier = parts[0].Trim();
        string? versionString = null;

        foreach (var part in parts.Skip(1))
        {
            var kv = part.Trim().Split('=');
            if (kv.Length == 2 && kv[0].Trim().Equals("Version", StringComparison.OrdinalIgnoreCase))
            {
                versionString = kv[1].Trim().TrimStart('v', 'V');
                break;
            }
        }

        if (versionString is null)
            return null;

        return identifier switch
        {
            ".NETCoreApp" => FormatNetCoreTfm(versionString),
            ".NETFramework" => FormatNetFrameworkTfm(versionString),
            ".NETStandard" => $"netstandard{versionString}",
            _ => null
        };
    }

    private static string FormatNetCoreTfm(string version)
    {
        if (Version.TryParse(version, out var v))
        {
            if (v.Major >= 5)
                return $"net{v.Major}.{v.Minor}";
            return $"netcoreapp{v.Major}.{v.Minor}";
        }
        return $"net{version}";
    }

    private static string FormatNetFrameworkTfm(string version)
    {
        if (Version.TryParse(version, out var v))
        {
            var major = v.Major;
            var minor = v.Minor;
            var build = v.Build > 0 ? v.Build : 0;
            if (build > 0)
                return $"net{major}{minor}{build}";
            return $"net{major}{minor}";
        }
        return $"net{version.Replace(".", "")}";
    }

    private static string? ParseFrameworkFromJson(JsonElement framework)
    {
        if (!framework.TryGetProperty("name", out var nameProp))
            return null;
        if (!framework.TryGetProperty("version", out var versionProp))
            return null;

        var name = nameProp.GetString();
        var version = versionProp.GetString();
        if (name is null || version is null)
            return null;

        if (!Version.TryParse(version, out var v))
            return null;

        return name switch
        {
            "Microsoft.NETCore.App" => v.Major >= 5 ? $"net{v.Major}.{v.Minor}" : $"netcoreapp{v.Major}.{v.Minor}",
            "Microsoft.AspNetCore.App" => v.Major >= 5 ? $"net{v.Major}.{v.Minor}" : $"netcoreapp{v.Major}.{v.Minor}",
            "Microsoft.WindowsDesktop.App" => v.Major >= 5 ? $"net{v.Major}.{v.Minor}" : $"netcoreapp{v.Major}.{v.Minor}",
            _ => null
        };
    }
}
