using Microsoft.Extensions.Logging.Abstractions;
using SharpRecon.Infrastructure;
using SharpRecon.Infrastructure.Resolution;
using Shouldly;
using Xunit;

namespace SharpRecon.Tests.Infrastructure.Resolution;

public sealed class AssemblyPathResolverTests
{
    private readonly PackageCache _packageCache = new();
    private readonly FrameworkAssemblyResolver _frameworkResolver = new(NullLogger<FrameworkAssemblyResolver>.Instance);

    private AssemblyPathResolver CreateResolver()
    {
        var nuspecReader = new NuspecReader();
        var cacheResolver = new GlobalCacheAssemblyResolver(_packageCache, nuspecReader);
        var dependencyResolver = new NuGetDependencyResolver(
            _packageCache,
            NullLogger<NuGetDependencyResolver>.Instance);

        return new AssemblyPathResolver(
            _packageCache,
            _frameworkResolver,
            cacheResolver,
            dependencyResolver,
            NullLogger<AssemblyPathResolver>.Instance);
    }

    [Fact]
    public async Task Resolve_NewtonsoftJson_ResolvesFrameworkAndPackageAssemblies()
    {
        if (!_packageCache.IsPackageCached("Newtonsoft.Json", "13.0.3"))
            return;

        var resolver = CreateResolver();

        var tfms = _packageCache.GetAvailableTfms("Newtonsoft.Json", "13.0.3");
        var tfm = tfms.FirstOrDefault(t => t.StartsWith("net", StringComparison.OrdinalIgnoreCase)
            && !t.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase))
            ?? tfms.First();

        var result = await resolver.ResolveAsync(
            "Newtonsoft.Json", "13.0.3", tfm, "Newtonsoft.Json",
            preferRef: true, CancellationToken.None);

        result.PrimaryAssemblyPath.ShouldNotBeNullOrEmpty();
        result.PrimaryAssemblyPath.ShouldEndWith(".dll");

        result.AllAssemblyPaths.ShouldNotBeEmpty();
        result.AllAssemblyPaths.ShouldContain(result.PrimaryAssemblyPath);

        result.AllAssemblyPaths.Count.ShouldBeGreaterThan(1);
    }

    [Fact]
    public async Task Resolve_MissingAssembly_ReportsUnresolved()
    {
        if (!_packageCache.IsPackageCached("Newtonsoft.Json", "13.0.3"))
            return;

        var resolver = CreateResolver();

        var tfms = _packageCache.GetAvailableTfms("Newtonsoft.Json", "13.0.3");
        var tfm = tfms.First();

        var result = await resolver.ResolveAsync(
            "Newtonsoft.Json", "13.0.3", tfm, "NonExistent.Assembly",
            preferRef: true, CancellationToken.None);

        result.PrimaryAssemblyPath.ShouldBeEmpty();
        result.UnresolvedDependencies.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Resolve_CachesResults()
    {
        if (!_packageCache.IsPackageCached("Newtonsoft.Json", "13.0.3"))
            return;

        var resolver = CreateResolver();

        var tfms = _packageCache.GetAvailableTfms("Newtonsoft.Json", "13.0.3");
        var tfm = tfms.First();

        var result1 = await resolver.ResolveAsync(
            "Newtonsoft.Json", "13.0.3", tfm, "Newtonsoft.Json",
            preferRef: true, CancellationToken.None);

        var result2 = await resolver.ResolveAsync(
            "Newtonsoft.Json", "13.0.3", tfm, "Newtonsoft.Json",
            preferRef: true, CancellationToken.None);

        ReferenceEquals(result1, result2).ShouldBeTrue();
    }

    [Fact]
    public async Task Resolve_NonCachedPackage_ReportsUnresolved()
    {
        var resolver = CreateResolver();

        var result = await resolver.ResolveAsync(
            "SomePackageThatDoesNotExist", "999.0.0", "net8.0", "SomeAssembly",
            preferRef: true, CancellationToken.None);

        result.PrimaryAssemblyPath.ShouldBeEmpty();
        result.UnresolvedDependencies.ShouldNotBeEmpty();
    }
}
