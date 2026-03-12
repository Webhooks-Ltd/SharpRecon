using Microsoft.Extensions.Logging.Abstractions;
using SharpRecon.Infrastructure;
using SharpRecon.Infrastructure.Resolution;
using SharpRecon.Inspection;
using Shouldly;
using Xunit;

namespace SharpRecon.Tests.Inspection;

public sealed class AssemblyInspectorTests
{
    private const string PackageId = "Newtonsoft.Json";
    private const string Version = "13.0.3";
    private const string Tfm = "net6.0";
    private const string AssemblyName = "Newtonsoft.Json";

    private readonly PackageCache _packageCache = new();

    private AssemblyInspector CreateInspector()
    {
        var frameworkResolver = new FrameworkAssemblyResolver(NullLogger<FrameworkAssemblyResolver>.Instance);
        var nuspecReader = new NuspecReader();
        var cacheResolver = new GlobalCacheAssemblyResolver(_packageCache, nuspecReader);
        var dependencyResolver = new NuGetDependencyResolver(
            _packageCache, NullLogger<NuGetDependencyResolver>.Instance);
        var pathResolver = new AssemblyPathResolver(
            _packageCache, frameworkResolver, cacheResolver, dependencyResolver,
            NullLogger<AssemblyPathResolver>.Instance);
        var xmlDocParser = new XmlDocParser();

        var assemblySource = new NuGetAssemblySource(_packageCache);
        return new AssemblyInspector(assemblySource, pathResolver, xmlDocParser);
    }

    [Fact]
    public async Task GetTypesAsync_NewtonsoftJson_ContainsJsonConvert()
    {
        if (!_packageCache.IsPackageCached(PackageId, Version)) return;

        var inspector = CreateInspector();

        var result = await inspector.GetTypesAsync(
            PackageId, Version, Tfm, AssemblyName, null, CancellationToken.None);

        result.Types.ShouldNotBeEmpty();

        var jsonConvert = result.Types.FirstOrDefault(
            t => t.FullName == "Newtonsoft.Json.JsonConvert");
        jsonConvert.ShouldNotBeNull();
        jsonConvert.Kind.ShouldBe("class");
    }

    [Fact]
    public async Task SearchTypesAsync_JsonConvert_IsFound()
    {
        if (!_packageCache.IsPackageCached(PackageId, Version)) return;

        var inspector = CreateInspector();

        var result = await inspector.SearchTypesAsync(
            PackageId, Version, Tfm, AssemblyName, "JsonConvert", 100, CancellationToken.None);

        result.Matches.ShouldNotBeEmpty();
        result.Matches.ShouldContain(m => m.FullName == "Newtonsoft.Json.JsonConvert");
    }

    [Fact]
    public async Task GetTypeDetailAsync_JsonConvert_ReturnsDeclarationAndMembers()
    {
        if (!_packageCache.IsPackageCached(PackageId, Version)) return;

        var inspector = CreateInspector();

        var result = await inspector.GetTypeDetailAsync(
            PackageId, Version, Tfm, AssemblyName, "Newtonsoft.Json.JsonConvert", false, CancellationToken.None);

        result.TypeDeclaration.ShouldContain("public static class JsonConvert");

        var memberKinds = result.MemberGroups.Select(g => g.Kind).ToList();
        memberKinds.ShouldContain("Methods");

        var methods = result.MemberGroups.First(g => g.Kind == "Methods");
        methods.Members.ShouldContain(m => m.Name == "SerializeObject");
    }

    [Fact]
    public async Task GetMemberDetailAsync_SerializeObject_ReturnsMultipleOverloads()
    {
        if (!_packageCache.IsPackageCached(PackageId, Version)) return;

        var inspector = CreateInspector();

        var result = await inspector.GetMemberDetailAsync(
            PackageId, Version, Tfm, AssemblyName,
            "Newtonsoft.Json.JsonConvert", "SerializeObject", null, CancellationToken.None);

        result.Overloads.Count.ShouldBeGreaterThan(1);
        result.Overloads.ShouldAllBe(o => o.Signature.Contains("SerializeObject"));
        result.Overloads.ShouldAllBe(o => o.Summary != null);
    }

    [Fact]
    public async Task GetMemberDetailAsync_WithParameterTypes_ReturnsSingleOverload()
    {
        if (!_packageCache.IsPackageCached(PackageId, Version)) return;

        var inspector = CreateInspector();

        var result = await inspector.GetMemberDetailAsync(
            PackageId, Version, Tfm, AssemblyName,
            "Newtonsoft.Json.JsonConvert", "SerializeObject",
            ["System.Object", "Newtonsoft.Json.Formatting"],
            CancellationToken.None);

        result.Overloads.Count.ShouldBe(1);
        result.Overloads[0].Signature.ShouldContain("SerializeObject");
    }

    [Fact]
    public async Task GetMemberDetailAsync_Ctor_StaticClassHasNoConstructors()
    {
        if (!_packageCache.IsPackageCached(PackageId, Version)) return;

        var inspector = CreateInspector();

        Should.Throw<InvalidOperationException>(async () =>
            await inspector.GetMemberDetailAsync(
                PackageId, Version, Tfm, AssemblyName,
                "Newtonsoft.Json.JsonConvert", ".ctor", null, CancellationToken.None));
    }
}
