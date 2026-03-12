using Microsoft.Extensions.Logging.Abstractions;
using SharpRecon.Decompilation;
using SharpRecon.Infrastructure;
using SharpRecon.Infrastructure.Resolution;
using Shouldly;
using Xunit;

namespace SharpRecon.Tests.Decompilation;

public sealed class AssemblyDecompilerTests
{
    private const string PackageId = "Newtonsoft.Json";
    private const string Version = "13.0.3";
    private const string Tfm = "net6.0";

    private readonly PackageCache _packageCache = new();

    private AssemblyDecompiler CreateDecompiler()
    {
        var frameworkResolver = new FrameworkAssemblyResolver(NullLogger<FrameworkAssemblyResolver>.Instance);
        var nuspecReader = new NuspecReader();
        var cacheResolver = new GlobalCacheAssemblyResolver(_packageCache, nuspecReader);
        var dependencyResolver = new NuGetDependencyResolver(
            _packageCache, NullLogger<NuGetDependencyResolver>.Instance);
        var pathResolver = new AssemblyPathResolver(
            _packageCache, frameworkResolver, cacheResolver, dependencyResolver,
            NullLogger<AssemblyPathResolver>.Instance);

        var assemblySource = new NuGetAssemblySource(_packageCache);
        return new AssemblyDecompiler(assemblySource, pathResolver);
    }

    [Fact]
    public async Task DecompileTypeAsync_JsonConvert_ContainsClassAndMethods()
    {
        if (!_packageCache.IsPackageCached(PackageId, Version)) return;

        var decompiler = CreateDecompiler();

        var result = await decompiler.DecompileTypeAsync(
            PackageId, Version, Tfm, "Newtonsoft.Json.JsonConvert", CancellationToken.None);

        result.Source.ShouldContain("class JsonConvert");
        result.Source.ShouldContain("SerializeObject");
        result.Source.ShouldContain("{");
        result.Source.ShouldContain("return");
    }

    [Fact]
    public async Task DecompileMemberAsync_WithParameterTypes_ReturnsSingleOverload()
    {
        if (!_packageCache.IsPackageCached(PackageId, Version)) return;

        var decompiler = CreateDecompiler();

        var result = await decompiler.DecompileMemberAsync(
            PackageId, Version, Tfm, "Newtonsoft.Json.JsonConvert",
            "SerializeObject", ["System.Object"], CancellationToken.None);

        result.Source.ShouldContain("SerializeObject");
        result.Source.ShouldContain("{");
    }

    [Fact]
    public async Task DecompileMemberAsync_WithoutParameterTypes_ReturnsAllOverloads()
    {
        if (!_packageCache.IsPackageCached(PackageId, Version)) return;

        var decompiler = CreateDecompiler();

        var result = await decompiler.DecompileMemberAsync(
            PackageId, Version, Tfm, "Newtonsoft.Json.JsonConvert",
            "SerializeObject", null, CancellationToken.None);

        result.Source.ShouldContain("SerializeObject");

        var occurrences = result.Source.Split("SerializeObject").Length - 1;
        occurrences.ShouldBeGreaterThan(1);
    }
}
