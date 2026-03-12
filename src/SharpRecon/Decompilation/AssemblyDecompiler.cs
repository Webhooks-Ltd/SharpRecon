using System.Reflection;
using System.Reflection.Metadata;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;
using SharpRecon.Infrastructure;
using SharpRecon.Infrastructure.Resolution;
using SharpRecon.Inspection;

namespace SharpRecon.Decompilation;

internal sealed class AssemblyDecompiler
{
    private readonly IAssemblySource _assemblySource;
    private readonly AssemblyPathResolver _pathResolver;

    public AssemblyDecompiler(IAssemblySource assemblySource, AssemblyPathResolver pathResolver)
    {
        _assemblySource = assemblySource;
        _pathResolver = pathResolver;
    }

    public async Task<DecompileResult> DecompileTypeAsync(
        string packageId, string version, string tfm, string typeName, CancellationToken ct)
    {
        var (assemblyName, resolution) = await ResolveAssemblyForTypeAsync(packageId, version, tfm, typeName, ct);

        var source = await Task.Run(() =>
        {
            var decompiler = CreateDecompiler(resolution);
            var fullTypeName = new FullTypeName(typeName);
            return decompiler.DecompileTypeAsString(fullTypeName);
        }, ct);

        return new DecompileResult(source, resolution.UnresolvedDependencies);
    }

    public async Task<DecompileResult> DecompileMemberAsync(
        string packageId, string version, string tfm, string typeName, string memberName, string[]? parameterTypes, CancellationToken ct)
    {
        var (assemblyName, resolution) = await ResolveAssemblyForTypeAsync(packageId, version, tfm, typeName, ct);

        var source = await Task.Run(() =>
        {
            var decompiler = CreateDecompiler(resolution);
            var module = decompiler.TypeSystem.MainModule;
            var typeDefinition = module.GetTypeDefinition(new FullTypeName(typeName));

            if (typeDefinition is null)
                throw new InvalidOperationException($"Type '{typeName}' not found in assembly '{assemblyName}'.");

            var handles = FindMemberHandles(typeDefinition, memberName, parameterTypes);
            if (handles.Count == 0)
                throw new InvalidOperationException($"Member '{memberName}' not found on type '{typeName}'.");

            return decompiler.DecompileAsString(handles);
        }, ct);

        return new DecompileResult(source, resolution.UnresolvedDependencies);
    }

    private async Task<(string AssemblyName, AssemblyResolutionResult Resolution)> ResolveAssemblyForTypeAsync(
        string packageId, string version, string tfm, string typeName, CancellationToken ct)
    {
        var assemblies = _assemblySource.GetAssembliesForTfm(packageId, version, tfm);
        if (assemblies.Count == 0)
            throw new InvalidOperationException($"No assemblies found for {packageId} {version} ({tfm}).");

        foreach (var asmName in assemblies)
        {
            var resolution = await ResolveAssemblyAsync(packageId, version, tfm, asmName, preferRef: false, ct);
            if (resolution.PrimaryAssemblyPath == string.Empty)
                continue;

            if (TypeExistsInAssembly(resolution.PrimaryAssemblyPath, typeName))
                return (asmName, resolution);
        }

        var firstAsm = assemblies[0];
        var fallbackResolution = await ResolveAssemblyAsync(packageId, version, tfm, firstAsm, preferRef: false, ct);
        if (fallbackResolution.PrimaryAssemblyPath == string.Empty)
            throw new InvalidOperationException(
                $"Assembly '{firstAsm}' not found in {packageId} {version} ({tfm}).");

        return (firstAsm, fallbackResolution);
    }

    private async Task<AssemblyResolutionResult> ResolveAssemblyAsync(
        string packageId, string version, string tfm, string assemblyName, bool preferRef, CancellationToken ct)
    {
        if (packageId.StartsWith("local:", StringComparison.OrdinalIgnoreCase))
            return await _pathResolver.ResolveLocalAsync(packageId, version, tfm, assemblyName, _assemblySource, ct);

        return await _pathResolver.ResolveAsync(packageId, version, tfm, assemblyName, preferRef, ct);
    }

    private static bool TypeExistsInAssembly(string assemblyPath, string typeName)
    {
        try
        {
            using var peFile = new PEFile(assemblyPath);
            var metadata = peFile.Metadata;
            foreach (var typeDefHandle in metadata.TypeDefinitions)
            {
                var typeDef = metadata.GetTypeDefinition(typeDefHandle);
                var ns = metadata.GetString(typeDef.Namespace);
                var name = metadata.GetString(typeDef.Name);
                var fullName = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
                if (string.Equals(fullName, typeName, StringComparison.Ordinal))
                    return true;
            }
        }
        catch { }
        return false;
    }

    private static CSharpDecompiler CreateDecompiler(AssemblyResolutionResult resolution)
    {
        var settings = new DecompilerSettings(LanguageVersion.Latest)
        {
            ThrowOnAssemblyResolveErrors = false,
        };

        var resolver = new UniversalAssemblyResolver(
            resolution.PrimaryAssemblyPath,
            throwOnError: false,
            targetFramework: null);

        var searchDirs = resolution.AllAssemblyPaths
            .Select(Path.GetDirectoryName)
            .Where(d => d is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in searchDirs)
            resolver.AddSearchDirectory(dir!);

        return new CSharpDecompiler(resolution.PrimaryAssemblyPath, resolver, settings);
    }

    private static List<EntityHandle> FindMemberHandles(
        ITypeDefinition typeDefinition, string memberName, string[]? parameterTypes)
    {
        var handles = new List<EntityHandle>();

        if (memberName is ".ctor")
        {
            foreach (var method in typeDefinition.Methods)
            {
                if (!method.IsConstructor) continue;
                if (!method.Accessibility.IsPublicOrProtected()) continue;

                if (parameterTypes is not null && !MatchesParameterTypes(method, parameterTypes))
                    continue;

                if (!method.MetadataToken.IsNil)
                    handles.Add(method.MetadataToken);
            }
            return handles;
        }

        foreach (var method in typeDefinition.Methods)
        {
            if (method.Name != memberName) continue;
            if (method.IsConstructor || !method.Accessibility.IsPublicOrProtected()) continue;

            if (parameterTypes is not null && !MatchesParameterTypes(method, parameterTypes))
                continue;

            if (!method.MetadataToken.IsNil)
                handles.Add(method.MetadataToken);
        }

        if (handles.Count > 0) return handles;

        foreach (var prop in typeDefinition.Properties)
        {
            if (prop.Name != memberName) continue;
            if (!prop.Accessibility.IsPublicOrProtected()) continue;
            if (!prop.MetadataToken.IsNil)
                handles.Add(prop.MetadataToken);
        }

        if (handles.Count > 0) return handles;

        foreach (var field in typeDefinition.Fields)
        {
            if (field.Name != memberName) continue;
            if (!field.Accessibility.IsPublicOrProtected()) continue;
            if (!field.MetadataToken.IsNil)
                handles.Add(field.MetadataToken);
        }

        if (handles.Count > 0) return handles;

        foreach (var evt in typeDefinition.Events)
        {
            if (evt.Name != memberName) continue;
            if (!evt.Accessibility.IsPublicOrProtected()) continue;
            if (!evt.MetadataToken.IsNil)
                handles.Add(evt.MetadataToken);
        }

        return handles;
    }

    private static bool MatchesParameterTypes(IMethod method, string[] expectedTypes)
    {
        var parameters = method.Parameters;
        if (parameters.Count != expectedTypes.Length)
            return false;

        for (var i = 0; i < parameters.Count; i++)
        {
            var paramType = parameters[i].Type;
            var fullName = paramType.ReflectionName;
            if (!string.Equals(fullName, expectedTypes[i], StringComparison.Ordinal))
                return false;
        }
        return true;
    }
}

internal sealed record DecompileResult(string Source, IReadOnlyList<string> UnresolvedDependencies);

internal static class AccessibilityExtensions
{
    public static bool IsPublicOrProtected(this Accessibility accessibility)
    {
        return accessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal;
    }
}
