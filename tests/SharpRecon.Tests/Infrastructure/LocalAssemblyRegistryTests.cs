using SharpRecon.Infrastructure;
using Shouldly;
using Xunit;

namespace SharpRecon.Tests.Infrastructure;

public sealed class LocalAssemblyRegistryTests
{
    private readonly LocalAssemblyRegistry _registry = new();

    [Fact]
    public void Register_ValidManagedDll_ReturnsSyntheticIdWithLocalPrefix()
    {
        var path = typeof(LocalAssemblyRegistryTests).Assembly.Location;

        var result = _registry.Register(path);

        result.SyntheticId.ShouldStartWith("local:");
        result.AssemblyCount.ShouldBe(1);
        result.Version.ShouldBe("local");
    }

    [Fact]
    public void Register_ValidManagedDll_SyntheticIdContainsAssemblyName()
    {
        var path = typeof(LocalAssemblyRegistryTests).Assembly.Location;
        var expectedName = Path.GetFileNameWithoutExtension(path);

        var result = _registry.Register(path);

        result.SyntheticId.ShouldBe($"local:{expectedName}");
    }

    [Fact]
    public void Register_NonExistentPath_ThrowsFileNotFoundException()
    {
        var fakePath = Path.Combine(Path.GetTempPath(), "nonexistent_" + Guid.NewGuid() + ".dll");

        Should.Throw<FileNotFoundException>(() => _registry.Register(fakePath));
    }

    [Fact]
    public void Register_Directory_ReturnsCorrectAssemblyCount()
    {
        var directory = AppContext.BaseDirectory;

        var result = _registry.Register(directory);

        result.AssemblyCount.ShouldBeGreaterThan(0);
        result.SyntheticId.ShouldStartWith("local:");
    }

    [Fact]
    public void TryGet_UnregisteredId_ReturnsNull()
    {
        _registry.TryGet("local:nonexistent").ShouldBeNull();
    }

    [Fact]
    public void TryGet_AfterRegister_ReturnsRegistration()
    {
        var path = typeof(LocalAssemblyRegistryTests).Assembly.Location;
        var result = _registry.Register(path);

        var registration = _registry.TryGet(result.SyntheticId);

        registration.ShouldNotBeNull();
        registration.PrimaryPath.ShouldBe(Path.GetFullPath(path));
        registration.AssemblyPaths.ShouldContain(Path.GetFullPath(path));
    }

    [Fact]
    public void IsRegistered_BeforeRegister_ReturnsFalse()
    {
        _registry.IsRegistered("local:nonexistent").ShouldBeFalse();
    }

    [Fact]
    public void IsRegistered_AfterRegister_ReturnsTrue()
    {
        var path = typeof(LocalAssemblyRegistryTests).Assembly.Location;
        var result = _registry.Register(path);

        _registry.IsRegistered(result.SyntheticId).ShouldBeTrue();
    }

    [Fact]
    public void Register_SameFileTwice_ReplacesRegistration()
    {
        var path = typeof(LocalAssemblyRegistryTests).Assembly.Location;

        var result1 = _registry.Register(path);
        var result2 = _registry.Register(path);

        result1.SyntheticId.ShouldBe(result2.SyntheticId);
        _registry.TryGet(result1.SyntheticId).ShouldNotBeNull();
    }

    [Fact]
    public void Register_ValidFile_InfersTfm()
    {
        var path = typeof(LocalAssemblyRegistryTests).Assembly.Location;

        var result = _registry.Register(path);

        result.InferredTfm.ShouldNotBeNullOrWhiteSpace();
        result.InferredTfm.ShouldStartWith("net");
    }

    [Fact]
    public void Register_Directory_SyntheticIdIncludesParentAndDirName()
    {
        var directory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

        var result = _registry.Register(directory);

        var dirName = Path.GetFileName(directory);
        var parentDirName = Path.GetFileName(Path.GetDirectoryName(directory)!);
        result.SyntheticId.ShouldBe($"local:{parentDirName}/{dirName}");
    }
}
