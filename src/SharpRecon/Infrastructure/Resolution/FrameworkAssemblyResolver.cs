using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace SharpRecon.Infrastructure.Resolution;

public class FrameworkAssemblyResolver
{
    private readonly Dictionary<string, string[]> _targetingPacks = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _runtimeDirectory;
    private readonly ILogger<FrameworkAssemblyResolver> _logger;

    public FrameworkAssemblyResolver(ILogger<FrameworkAssemblyResolver> logger)
    {
        _logger = logger;
        _runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
        DiscoverTargetingPacks();
    }

    public IReadOnlyList<string> GetFrameworkAssemblyPaths(string tfm)
    {
        if (_targetingPacks.TryGetValue(tfm, out var paths))
            return paths;

        if (tfm.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("No targeting pack for {Tfm}, falling back to runtime directory", tfm);
            return GetRuntimeAssemblies();
        }

        _logger.LogDebug("No targeting pack for {Tfm}, falling back to runtime directory", tfm);
        return GetRuntimeAssemblies();
    }

    private string[] GetRuntimeAssemblies()
    {
        if (!Directory.Exists(_runtimeDirectory))
            return [];

        return Directory.GetFiles(_runtimeDirectory, "*.dll");
    }

    private void DiscoverTargetingPacks()
    {
        var dotnetRoot = ResolveDotnetRoot();
        if (dotnetRoot is null)
        {
            _logger.LogWarning("Could not resolve dotnet root directory. Targeting pack discovery skipped.");
            return;
        }

        var packsDir = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref");
        if (!Directory.Exists(packsDir))
        {
            _logger.LogWarning("Targeting packs directory not found at {PacksDir}", packsDir);
            return;
        }

        foreach (var versionDir in Directory.GetDirectories(packsDir))
        {
            var refDir = Path.Combine(versionDir, "ref");
            if (!Directory.Exists(refDir))
                continue;

            foreach (var tfmDir in Directory.GetDirectories(refDir))
            {
                var tfm = Path.GetFileName(tfmDir);
                var assemblies = Directory.GetFiles(tfmDir, "*.dll");
                if (assemblies.Length == 0)
                    continue;

                if (_targetingPacks.TryGetValue(tfm, out var existing))
                {
                    if (assemblies.Length > existing.Length)
                        _targetingPacks[tfm] = assemblies;
                }
                else
                {
                    _targetingPacks[tfm] = assemblies;
                }
            }
        }

        _logger.LogInformation("Discovered targeting packs for TFMs: {Tfms}", string.Join(", ", _targetingPacks.Keys));
    }

    private string? ResolveDotnetRoot()
    {
        var envRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(envRoot) && Directory.Exists(envRoot))
        {
            _logger.LogDebug("Resolved dotnet root from DOTNET_ROOT: {Root}", envRoot);
            return envRoot;
        }

        var runtimeDir = new DirectoryInfo(_runtimeDirectory);
        var candidate = runtimeDir;
        while (candidate is not null)
        {
            var sdkPath = Path.Combine(candidate.FullName, "sdk");
            if (Directory.Exists(sdkPath))
            {
                _logger.LogDebug("Resolved dotnet root by walking up from runtime directory: {Root}", candidate.FullName);
                return candidate.FullName;
            }
            candidate = candidate.Parent;
        }

        var defaultRoot = GetPlatformDefaultDotnetRoot();
        if (defaultRoot is not null && Directory.Exists(defaultRoot))
        {
            _logger.LogDebug("Resolved dotnet root from platform default: {Root}", defaultRoot);
            return defaultRoot;
        }

        return null;
    }

    private static string? GetPlatformDefaultDotnetRoot()
    {
        if (OperatingSystem.IsWindows())
            return @"C:\Program Files\dotnet";
        if (OperatingSystem.IsLinux())
            return "/usr/share/dotnet";
        if (OperatingSystem.IsMacOS())
            return "/usr/local/share/dotnet";
        return null;
    }
}
