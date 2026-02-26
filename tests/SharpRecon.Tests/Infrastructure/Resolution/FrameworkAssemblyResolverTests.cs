using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharpRecon.Infrastructure.Resolution;
using Shouldly;
using Xunit;

namespace SharpRecon.Tests.Infrastructure.Resolution;

public class FrameworkAssemblyResolverTests
{
    private readonly FrameworkAssemblyResolver _resolver = new(NullLogger<FrameworkAssemblyResolver>.Instance);

    [Fact]
    public void DiscoverAtLeastOneTargetingPack()
    {
        var paths = _resolver.GetFrameworkAssemblyPaths("net10.0");
        paths.ShouldNotBeEmpty();
    }

    [Fact]
    public void GetFrameworkAssemblyPaths_Net10_ReturnsNonEmpty()
    {
        var paths = _resolver.GetFrameworkAssemblyPaths("net10.0");
        paths.Count.ShouldBeGreaterThan(0);
        paths.ShouldAllBe(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetFrameworkAssemblyPaths_NonsenseTfm_FallsBackToRuntimeDirectory()
    {
        var paths = _resolver.GetFrameworkAssemblyPaths("net999.0");
        paths.ShouldNotBeEmpty();
        paths.ShouldAllBe(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetFrameworkAssemblyPaths_NetStandard20_ReturnsRuntimeAssemblies()
    {
        var paths = _resolver.GetFrameworkAssemblyPaths("netstandard2.0");
        paths.ShouldNotBeEmpty();
        paths.ShouldAllBe(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
    }
}
