using SharpRecon.Infrastructure;
using Shouldly;
using Xunit;

namespace SharpRecon.Tests.Infrastructure;

public sealed class AssemblyValidatorTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    private string CreateTempFile(byte[] content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.dll");
        File.WriteAllBytes(path, content);
        _tempFiles.Add(path);
        return path;
    }

    [Fact]
    public void IsManagedAssembly_ManagedDll_ReturnsTrue()
    {
        var path = typeof(Shouldly.Should).Assembly.Location;

        AssemblyValidator.IsManagedAssembly(path).ShouldBeTrue();
    }

    [Fact]
    public void IsManagedAssembly_GarbageBytes_ReturnsFalse()
    {
        var path = CreateTempFile([0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x01, 0x02, 0x03]);

        AssemblyValidator.IsManagedAssembly(path).ShouldBeFalse();
    }

    [Fact]
    public void IsManagedAssembly_NonExistentFile_ReturnsFalse()
    {
        AssemblyValidator.IsManagedAssembly(@"C:\nonexistent\fake.dll").ShouldBeFalse();
    }

    [Fact]
    public void ValidateForLoading_ValidManagedAssembly_ReturnsNull()
    {
        var path = typeof(Shouldly.Should).Assembly.Location;

        AssemblyValidator.ValidateForLoading(path).ShouldBeNull();
    }

    [Fact]
    public void ValidateForLoading_NonExistentPath_ReturnsError()
    {
        var path = Path.Combine(Path.GetTempPath(), "nonexistent_" + Guid.NewGuid() + ".dll");

        var result = AssemblyValidator.ValidateForLoading(path);

        result.ShouldNotBeNull();
        result.ShouldContain("File not found");
    }

    [Fact]
    public void ValidateForLoading_NonManagedFile_ReturnsError()
    {
        var path = CreateTempFile([0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x01, 0x02, 0x03]);

        var result = AssemblyValidator.ValidateForLoading(path);

        result.ShouldNotBeNull();
        result.ShouldContain("Not a managed .NET assembly");
    }

    [Fact]
    public void IsSingleFileBundle_RegularManagedAssembly_ReturnsFalse()
    {
        var path = typeof(Shouldly.Should).Assembly.Location;

        AssemblyValidator.IsSingleFileBundle(path).ShouldBeFalse();
    }

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            try { File.Delete(path); } catch { }
        }
    }
}
