using System.Collections.Concurrent;
using SharpRecon.Infrastructure.Resolution;

namespace SharpRecon.Infrastructure;

internal sealed class LocalAssemblyRegistry
{
    private readonly ConcurrentDictionary<string, LocalRegistration> _registrations = new(StringComparer.OrdinalIgnoreCase);
    private readonly AssemblyPathResolver? _pathResolver;

    public LocalAssemblyRegistry() { }

    public LocalAssemblyRegistry(AssemblyPathResolver pathResolver)
    {
        _pathResolver = pathResolver;
    }

    public LocalLoadResult Register(string path)
    {
        path = Path.GetFullPath(path);

        if (File.Exists(path))
            return RegisterFile(path);

        if (Directory.Exists(path))
            return RegisterDirectory(path);

        throw new FileNotFoundException($"Path not found: {path}");
    }

    public LocalRegistration? TryGet(string syntheticId) =>
        _registrations.TryGetValue(syntheticId, out var reg) ? reg : null;

    public bool IsRegistered(string syntheticId) =>
        _registrations.ContainsKey(syntheticId);

    private LocalLoadResult RegisterFile(string filePath)
    {
        var error = AssemblyValidator.ValidateForLoading(filePath);
        if (error is not null)
            throw new InvalidOperationException(error);

        var assemblyName = Path.GetFileNameWithoutExtension(filePath);
        var syntheticId = $"local:{assemblyName}";
        var directory = Path.GetDirectoryName(filePath)!;

        var xmlDocPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var xmlPath = Path.Combine(directory, assemblyName + ".xml");
        if (File.Exists(xmlPath))
            xmlDocPaths[assemblyName] = xmlPath;

        var inferredTfm = TfmInferrer.InferTfm(filePath);
        var hasMixedMode = AssemblyValidator.IsMixedMode(filePath);

        var registration = new LocalRegistration(
            PrimaryPath: filePath,
            AssemblyPaths: [filePath],
            XmlDocPaths: xmlDocPaths,
            InferredTfm: inferredTfm ?? GetRunningTfm(),
            DepsJsonPath: FindDepsJson(directory, assemblyName),
            HasMixedModeAssemblies: hasMixedMode);

        _registrations[syntheticId] = registration;
        _pathResolver?.InvalidateLocalCache(syntheticId);

        return new LocalLoadResult(
            SyntheticId: syntheticId,
            InferredTfm: registration.InferredTfm,
            AssemblyCount: 1,
            NativeSkipCount: 0,
            Path: filePath);
    }

    private LocalLoadResult RegisterDirectory(string directoryPath)
    {
        var allFiles = Directory.EnumerateFiles(directoryPath, "*.*")
            .Where(f =>
            {
                var ext = Path.GetExtension(f);
                return ext.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                       || ext.Equals(".exe", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        var managed = new List<string>();
        var nativeSkipCount = 0;
        var hasMixedMode = false;

        var dllNames = new HashSet<string>(
            allFiles.Where(f => Path.GetExtension(f).Equals(".dll", StringComparison.OrdinalIgnoreCase))
                .Select(f => Path.GetFileNameWithoutExtension(f)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var file in allFiles)
        {
            if (Path.GetExtension(file).Equals(".exe", StringComparison.OrdinalIgnoreCase)
                && dllNames.Contains(Path.GetFileNameWithoutExtension(file)))
                continue;

            if (!AssemblyValidator.IsManagedAssembly(file))
            {
                nativeSkipCount++;
                continue;
            }

            if (AssemblyValidator.IsMixedMode(file))
                hasMixedMode = true;

            managed.Add(file);
        }

        if (managed.Count == 0)
            throw new InvalidOperationException($"No .NET assemblies found in directory: {directoryPath}");

        var dirName = Path.GetFileName(directoryPath);
        var parentDirName = Path.GetFileName(Path.GetDirectoryName(directoryPath)!);
        var syntheticId = $"local:{parentDirName}/{dirName}";

        var xmlDocPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assemblyPath in managed)
        {
            var asmName = Path.GetFileNameWithoutExtension(assemblyPath);
            var xmlPath = Path.Combine(directoryPath, asmName + ".xml");
            if (File.Exists(xmlPath))
                xmlDocPaths[asmName] = xmlPath;
        }

        var inferredTfm = TfmInferrer.InferTfmForDirectory(directoryPath, managed);

        var registration = new LocalRegistration(
            PrimaryPath: directoryPath,
            AssemblyPaths: managed.OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).ToList(),
            XmlDocPaths: xmlDocPaths,
            InferredTfm: inferredTfm ?? GetRunningTfm(),
            DepsJsonPath: FindDepsJsonInDirectory(directoryPath),
            HasMixedModeAssemblies: hasMixedMode);

        _registrations[syntheticId] = registration;
        _pathResolver?.InvalidateLocalCache(syntheticId);

        return new LocalLoadResult(
            SyntheticId: syntheticId,
            InferredTfm: registration.InferredTfm,
            AssemblyCount: managed.Count,
            NativeSkipCount: nativeSkipCount,
            Path: directoryPath);
    }

    private static string? FindDepsJson(string directory, string assemblyName)
    {
        var path = Path.Combine(directory, assemblyName + ".deps.json");
        return File.Exists(path) ? path : null;
    }

    private static string? FindDepsJsonInDirectory(string directory)
    {
        var depsFiles = Directory.EnumerateFiles(directory, "*.deps.json").ToList();
        return depsFiles.Count > 0 ? depsFiles[0] : null;
    }

    private static string GetRunningTfm()
    {
        var version = Environment.Version;
        return $"net{version.Major}.{version.Minor}";
    }
}

internal record LocalRegistration(
    string PrimaryPath,
    IReadOnlyList<string> AssemblyPaths,
    IReadOnlyDictionary<string, string> XmlDocPaths,
    string InferredTfm,
    string? DepsJsonPath,
    bool HasMixedModeAssemblies);

internal record LocalLoadResult(
    string SyntheticId,
    string InferredTfm,
    int AssemblyCount,
    int NativeSkipCount,
    string Path,
    string Version = "local");
