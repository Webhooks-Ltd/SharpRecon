using System.Reflection;
using SharpRecon.Infrastructure;
using SharpRecon.Infrastructure.Resolution;
using SharpRecon.Inspection.Models;

namespace SharpRecon.Inspection;

internal sealed class AssemblyInspector : IAssemblyInspector
{
    private readonly IPackageCache _packageCache;
    private readonly AssemblyPathResolver _pathResolver;
    private readonly XmlDocParser _xmlDocParser;

    public AssemblyInspector(
        IPackageCache packageCache,
        AssemblyPathResolver pathResolver,
        XmlDocParser xmlDocParser)
    {
        _packageCache = packageCache;
        _pathResolver = pathResolver;
        _xmlDocParser = xmlDocParser;
    }

    public async Task<TypeListResult> GetTypesAsync(
        string packageId, string version, string? tfm, string assemblyName, string? ns, CancellationToken ct)
    {
        tfm ??= SelectBestTfm(packageId, version);
        var resolution = await _pathResolver.ResolveAsync(packageId, version, tfm, assemblyName, preferRef: true, ct);
        if (resolution.PrimaryAssemblyPath == string.Empty)
            throw new InvalidOperationException(
                $"Assembly '{assemblyName}' not found in {packageId} {version} ({tfm}).");

        using var mlc = MetadataLoadContextFactory.Create(resolution.AllAssemblyPaths);
        var assembly = mlc.LoadFromAssemblyPath(resolution.PrimaryAssemblyPath);
        var types = GetPublicTypes(assembly);

        if (ns is not null)
            types = types.Where(t => string.Equals(t.Namespace, ns, StringComparison.Ordinal)).ToArray();

        var entries = types.Select(t => new TypeListEntry(
            t.FullName ?? t.Name,
            t.Namespace ?? "",
            GetTypeKind(t))).ToList();

        return new TypeListResult(entries, resolution.UnresolvedDependencies);
    }

    public async Task<TypeSearchResult> SearchTypesAsync(
        string packageId, string version, string? tfm, string? assemblyName, string query, int maxResults, CancellationToken ct)
    {
        tfm ??= SelectBestTfm(packageId, version);

        var assemblyNames = assemblyName is not null
            ? [assemblyName]
            : _packageCache.GetAssembliesForTfm(packageId, version, tfm);

        var allMatches = new List<TypeSearchEntry>();
        IReadOnlyList<string> lastUnresolved = [];

        foreach (var asmName in assemblyNames)
        {
            var resolution = await _pathResolver.ResolveAsync(packageId, version, tfm, asmName, preferRef: true, ct);
            if (resolution.PrimaryAssemblyPath == string.Empty)
                continue;

            lastUnresolved = resolution.UnresolvedDependencies;

            using var mlc = MetadataLoadContextFactory.Create(resolution.AllAssemblyPaths);
            var assembly = mlc.LoadFromAssemblyPath(resolution.PrimaryAssemblyPath);

            foreach (var type in GetPublicTypes(assembly))
            {
                var fullName = type.FullName ?? type.Name;
                if (fullName.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    allMatches.Add(new TypeSearchEntry(fullName, GetTypeKind(type), asmName));
                }
            }
        }

        var totalCount = allMatches.Count;
        var capped = allMatches.Take(maxResults).ToList();

        return new TypeSearchResult(capped, totalCount, lastUnresolved);
    }

    public async Task<TypeDetailResult> GetTypeDetailAsync(
        string packageId, string version, string? tfm, string? assemblyName, string typeName, CancellationToken ct)
    {
        tfm ??= SelectBestTfm(packageId, version);
        var (resolution, mlc, type) = await ResolveTypeAsync(packageId, version, tfm, assemblyName, typeName, preferRef: true, ct);
        using (mlc)
        {
            var xmlDocs = LoadXmlDocs(packageId, version, tfm, GetAssemblyFileName(resolution.PrimaryAssemblyPath));
            var typeDocId = $"T:{typeName}";
            var typeSummary = xmlDocs?.GetDocumentation(typeDocId)?.Summary;
            var typeDecl = TypeRenderer.RenderTypeDeclaration(type);
            var memberGroups = BuildMemberGroups(type, xmlDocs);

            return new TypeDetailResult(typeDecl, typeSummary, memberGroups, resolution.UnresolvedDependencies);
        }
    }

    public async Task<MemberDetailResult> GetMemberDetailAsync(
        string packageId, string version, string? tfm, string? assemblyName, string typeName, string memberName, string[]? parameterTypes, CancellationToken ct)
    {
        tfm ??= SelectBestTfm(packageId, version);
        var (resolution, mlc, type) = await ResolveTypeAsync(packageId, version, tfm, assemblyName, typeName, preferRef: true, ct);
        using (mlc)
        {
            var xmlDocs = LoadXmlDocs(packageId, version, tfm, GetAssemblyFileName(resolution.PrimaryAssemblyPath));
            var overloads = GetMemberOverloads(type, memberName, parameterTypes, xmlDocs);

            if (overloads.Count == 0)
            {
                var available = GetAvailableMemberNames(type);
                throw new InvalidOperationException(
                    $"Member '{memberName}' not found on type '{typeName}'. Available members: {string.Join(", ", available)}");
            }

            return new MemberDetailResult(typeName, memberName, overloads, resolution.UnresolvedDependencies);
        }
    }

    private async Task<(AssemblyResolutionResult Resolution, MetadataLoadContext Mlc, Type Type)> ResolveTypeAsync(
        string packageId, string version, string tfm, string? assemblyName, string typeName, bool preferRef, CancellationToken ct)
    {
        if (assemblyName is not null)
        {
            var resolution = await _pathResolver.ResolveAsync(packageId, version, tfm, assemblyName, preferRef, ct);
            if (resolution.PrimaryAssemblyPath == string.Empty)
                throw new InvalidOperationException(
                    $"Assembly '{assemblyName}' not found in {packageId} {version} ({tfm}).");

            var mlc = MetadataLoadContextFactory.Create(resolution.AllAssemblyPaths);
            try
            {
                var assembly = mlc.LoadFromAssemblyPath(resolution.PrimaryAssemblyPath);
                var type = assembly.GetType(typeName)
                    ?? throw new InvalidOperationException(BuildTypeNotFoundMessage(typeName, assembly));
                return (resolution, mlc, type);
            }
            catch
            {
                mlc.Dispose();
                throw;
            }
        }

        var assemblies = _packageCache.GetAssembliesForTfm(packageId, version, tfm);
        foreach (var asmName in assemblies)
        {
            var resolution = await _pathResolver.ResolveAsync(packageId, version, tfm, asmName, preferRef, ct);
            if (resolution.PrimaryAssemblyPath == string.Empty)
                continue;

            var mlc = MetadataLoadContextFactory.Create(resolution.AllAssemblyPaths);
            var assembly = mlc.LoadFromAssemblyPath(resolution.PrimaryAssemblyPath);
            var type = assembly.GetType(typeName);
            if (type is not null && type.IsPublic)
                return (resolution, mlc, type);

            mlc.Dispose();
        }

        throw new InvalidOperationException(
            $"Type '{typeName}' not found in any assembly of {packageId} {version} ({tfm}). Ensure the type name is fully qualified (e.g. 'Newtonsoft.Json.JsonConvert').");
    }

    private string SelectBestTfm(string packageId, string version)
    {
        return TfmSelector.SelectBest(_packageCache, packageId, version);
    }

    private static Type[] GetPublicTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null).ToArray()!;
        }
    }

    private static string GetTypeKind(Type type)
    {
        if (type.IsEnum) return "enum";
        if (IsDelegate(type)) return "delegate";
        if (type.IsInterface) return "interface";
        if (type.IsValueType)
        {
            if (IsRecord(type)) return "record struct";
            return "struct";
        }
        if (IsRecord(type)) return "record";
        return "class";
    }

    private static bool IsDelegate(Type type)
    {
        var baseType = type.BaseType;
        while (baseType is not null)
        {
            if (baseType.FullName is "System.MulticastDelegate" or "System.Delegate")
                return true;
            baseType = baseType.BaseType;
        }
        return false;
    }

    private static bool IsRecord(Type type)
    {
        try
        {
            var cloneMethod = type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance);
            if (cloneMethod is not null) return true;

            if (type.IsValueType)
            {
                var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance);
                var printMembers = methods.FirstOrDefault(m =>
                    m.Name == "PrintMembers" &&
                    m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType.FullName == "System.Text.StringBuilder");
                if (printMembers is not null)
                {
                    var equalityOp = type.GetMethod("op_Equality", BindingFlags.Public | BindingFlags.Static);
                    if (equalityOp is not null) return true;
                }
            }
        }
        catch { }
        return false;
    }

    private XmlDocCollection? LoadXmlDocs(string packageId, string version, string tfm, string assemblyName)
    {
        var cacheKey = $"{packageId}/{version}/{tfm}/{assemblyName}";
        var xmlPath = ResolveXmlDocPath(packageId, version, tfm, assemblyName);
        if (xmlPath is null) return null;
        return _xmlDocParser.LoadForAssembly(xmlPath, cacheKey);
    }

    private string? ResolveXmlDocPath(string packageId, string version, string tfm, string assemblyName)
    {
        var packagePath = _packageCache.GetPackagePath(packageId, version);
        var xmlFileName = assemblyName + ".xml";

        var refXml = Path.Combine(packagePath, "ref", tfm, xmlFileName);
        if (File.Exists(refXml)) return refXml;

        var libXml = Path.Combine(packagePath, "lib", tfm, xmlFileName);
        if (File.Exists(libXml)) return libXml;

        return null;
    }

    private static string GetAssemblyFileName(string assemblyPath)
    {
        return Path.GetFileNameWithoutExtension(assemblyPath);
    }

    private static IReadOnlyList<MemberGroup> BuildMemberGroups(Type type, XmlDocCollection? xmlDocs)
    {
        var groups = new List<MemberGroup>();

        var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(c => !c.IsPrivate && !c.IsAssembly);
        var ctorMembers = ctors.Select(c => new MemberSignature(
            ".ctor",
            TypeRenderer.RenderConstructorSignature(c),
            GetMemberDoc(xmlDocs, type, c))).ToList();
        if (ctorMembers.Count > 0)
            groups.Add(new MemberGroup("Constructors", ctorMembers));

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(p => IsPublicOrProtected(p));
        var propMembers = properties.Select(p => new MemberSignature(
            p.Name,
            TypeRenderer.RenderPropertySignature(p),
            GetPropertyDoc(xmlDocs, type, p))).ToList();
        if (propMembers.Count > 0)
            groups.Add(new MemberGroup("Properties", propMembers));

        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => !m.IsSpecialName && (m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly));
        var methodMembers = methods.Select(m => new MemberSignature(
            m.Name,
            TypeRenderer.RenderMethodSignature(m),
            GetMemberDoc(xmlDocs, type, m))).ToList();
        if (methodMembers.Count > 0)
            groups.Add(new MemberGroup("Methods", methodMembers));

        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(f => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly);
        var fieldMembers = fields.Select(f => new MemberSignature(
            f.Name,
            TypeRenderer.RenderFieldSignature(f),
            GetFieldDoc(xmlDocs, type, f))).ToList();
        if (fieldMembers.Count > 0)
            groups.Add(new MemberGroup("Fields", fieldMembers));

        var events = type.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(e => IsPublicOrProtected(e));
        var eventMembers = events.Select(e => new MemberSignature(
            e.Name,
            TypeRenderer.RenderEventSignature(e),
            GetEventDoc(xmlDocs, type, e))).ToList();
        if (eventMembers.Count > 0)
            groups.Add(new MemberGroup("Events", eventMembers));

        return groups;
    }

    private static bool IsPublicOrProtected(PropertyInfo prop)
    {
        var getter = prop.GetGetMethod(true);
        var setter = prop.GetSetMethod(true);
        return (getter is not null && (getter.IsPublic || getter.IsFamily || getter.IsFamilyOrAssembly))
            || (setter is not null && (setter.IsPublic || setter.IsFamily || setter.IsFamilyOrAssembly));
    }

    private static bool IsPublicOrProtected(EventInfo evt)
    {
        var add = evt.GetAddMethod(true);
        return add is not null && (add.IsPublic || add.IsFamily || add.IsFamilyOrAssembly);
    }

    private static string? GetMemberDoc(XmlDocCollection? docs, Type type, MethodBase method)
    {
        if (docs is null) return null;
        var docId = BuildMethodDocId(type, method);
        return docs.GetDocumentation(docId)?.Summary;
    }

    private static string? GetPropertyDoc(XmlDocCollection? docs, Type type, PropertyInfo prop)
    {
        if (docs is null) return null;
        var docId = $"P:{type.FullName}.{prop.Name}";
        return docs.GetDocumentation(docId)?.Summary;
    }

    private static string? GetFieldDoc(XmlDocCollection? docs, Type type, FieldInfo field)
    {
        if (docs is null) return null;
        var docId = $"F:{type.FullName}.{field.Name}";
        return docs.GetDocumentation(docId)?.Summary;
    }

    private static string? GetEventDoc(XmlDocCollection? docs, Type type, EventInfo evt)
    {
        if (docs is null) return null;
        var docId = $"E:{type.FullName}.{evt.Name}";
        return docs.GetDocumentation(docId)?.Summary;
    }

    private static string BuildMethodDocId(Type type, MethodBase method)
    {
        var name = method is ConstructorInfo ? "#ctor" : method.Name;
        var docId = $"M:{type.FullName}.{name}";

        var parameters = method.GetParameters();
        if (parameters.Length > 0)
        {
            var paramTypes = string.Join(",", parameters.Select(p => RenderDocIdType(p.ParameterType)));
            docId += $"({paramTypes})";
        }

        return docId;
    }

    private static string RenderDocIdType(Type type)
    {
        if (type.IsByRef)
            return RenderDocIdType(type.GetElementType()!) + "@";

        if (type.IsArray)
        {
            var rank = type.GetArrayRank();
            var suffix = rank == 1 ? "[]" : "[" + new string(',', rank - 1) + "]";
            return RenderDocIdType(type.GetElementType()!) + suffix;
        }

        if (type.IsGenericType && !type.IsGenericTypeDefinition)
        {
            var def = type.GetGenericTypeDefinition();
            var baseName = def.FullName ?? def.Name;
            var backtick = baseName.IndexOf('`');
            if (backtick >= 0) baseName = baseName[..backtick];
            var args = string.Join(",", type.GetGenericArguments().Select(RenderDocIdType));
            return $"{baseName}{{{args}}}";
        }

        if (type.IsGenericParameter)
        {
            return (type.DeclaringMethod is not null ? "``" : "`") + type.GenericParameterPosition;
        }

        return type.FullName ?? type.Name;
    }

    private IReadOnlyList<MemberOverload> GetMemberOverloads(
        Type type, string memberName, string[]? parameterTypes, XmlDocCollection? xmlDocs)
    {
        var overloads = new List<MemberOverload>();

        if (memberName is ".ctor")
        {
            var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(c => c.IsPublic || c.IsFamily || c.IsFamilyOrAssembly);

            foreach (var ctor in ctors)
            {
                if (parameterTypes is not null && !MatchesParameterTypes(ctor.GetParameters(), parameterTypes))
                    continue;

                var docId = BuildMethodDocId(type, ctor);
                var doc = xmlDocs?.GetDocumentation(docId);
                overloads.Add(new MemberOverload(
                    TypeRenderer.RenderConstructorSignature(ctor),
                    doc?.Summary,
                    doc?.Params ?? new Dictionary<string, string>(),
                    doc?.Returns,
                    doc?.Exceptions ?? [],
                    doc?.Remarks));
            }
            return overloads;
        }

        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => string.Equals(m.Name, memberName, StringComparison.Ordinal) && !m.IsSpecialName && (m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly));

        foreach (var method in methods)
        {
            if (parameterTypes is not null && !MatchesParameterTypes(method.GetParameters(), parameterTypes))
                continue;

            var docId = BuildMethodDocId(type, method);
            var doc = xmlDocs?.GetDocumentation(docId);
            overloads.Add(new MemberOverload(
                TypeRenderer.RenderMethodSignature(method),
                doc?.Summary,
                doc?.Params ?? new Dictionary<string, string>(),
                doc?.Returns,
                doc?.Exceptions ?? [],
                doc?.Remarks));
        }

        if (overloads.Count > 0) return overloads;

        var property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
        if (property is not null && IsPublicOrProtected(property))
        {
            var docId = $"P:{type.FullName}.{property.Name}";
            var doc = xmlDocs?.GetDocumentation(docId);
            overloads.Add(new MemberOverload(
                TypeRenderer.RenderPropertySignature(property),
                doc?.Summary,
                doc?.Params ?? new Dictionary<string, string>(),
                doc?.Returns,
                doc?.Exceptions ?? [],
                doc?.Remarks));
            return overloads;
        }

        var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
        if (field is not null && (field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly))
        {
            var docId = $"F:{type.FullName}.{field.Name}";
            var doc = xmlDocs?.GetDocumentation(docId);
            overloads.Add(new MemberOverload(
                TypeRenderer.RenderFieldSignature(field),
                doc?.Summary,
                doc?.Params ?? new Dictionary<string, string>(),
                doc?.Returns,
                doc?.Exceptions ?? [],
                doc?.Remarks));
            return overloads;
        }

        var evt = type.GetEvent(memberName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
        if (evt is not null && IsPublicOrProtected(evt))
        {
            var docId = $"E:{type.FullName}.{evt.Name}";
            var doc = xmlDocs?.GetDocumentation(docId);
            overloads.Add(new MemberOverload(
                TypeRenderer.RenderEventSignature(evt),
                doc?.Summary,
                doc?.Params ?? new Dictionary<string, string>(),
                doc?.Returns,
                doc?.Exceptions ?? [],
                doc?.Remarks));
            return overloads;
        }

        return overloads;
    }

    private static bool MatchesParameterTypes(ParameterInfo[] parameters, string[] expectedTypes)
    {
        if (parameters.Length != expectedTypes.Length)
            return false;

        for (var i = 0; i < parameters.Length; i++)
        {
            var paramType = parameters[i].ParameterType;
            if (paramType.IsByRef)
                paramType = paramType.GetElementType()!;

            var paramFullName = paramType.FullName ?? paramType.Name;
            if (!string.Equals(paramFullName, expectedTypes[i], StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    private static string BuildTypeNotFoundMessage(string typeName, Assembly assembly)
    {
        var publicTypes = GetPublicTypes(assembly);
        var simpleName = typeName.Contains('.') ? typeName[(typeName.LastIndexOf('.') + 1)..] : typeName;
        var suggestions = publicTypes
            .Where(t => (t.FullName ?? t.Name).Contains(simpleName, StringComparison.OrdinalIgnoreCase))
            .Select(t => t.FullName ?? t.Name)
            .Take(5)
            .ToList();

        var message = $"Type '{typeName}' not found in assembly.";
        if (suggestions.Count > 0)
            message += $" Similar types: {string.Join(", ", suggestions)}";
        return message;
    }

    private static IReadOnlyList<string> GetAvailableMemberNames(Type type)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
        if (ctors.Length > 0) names.Add(".ctor");

        foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            if (!m.IsSpecialName && (m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly))
                names.Add(m.Name);
        }

        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            if (IsPublicOrProtected(p)) names.Add(p.Name);
        }

        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            if (f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly) names.Add(f.Name);
        }

        foreach (var e in type.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            if (IsPublicOrProtected(e)) names.Add(e.Name);
        }

        return names.OrderBy(n => n, StringComparer.Ordinal).ToList();
    }
}
