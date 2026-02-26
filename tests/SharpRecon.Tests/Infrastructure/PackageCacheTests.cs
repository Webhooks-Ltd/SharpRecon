using System.Runtime.InteropServices;
using SharpRecon.Infrastructure;
using Shouldly;
using Xunit;

namespace SharpRecon.Tests.Infrastructure;

public sealed class PackageCacheTests : IDisposable
{
    private readonly string _tempDir;
    private readonly PackageCache _cache;

    public PackageCacheTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SharpReconTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _cache = new PackageCache(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string CreatePackageLayout(string packageId, string version)
    {
        var packageDir = Path.Combine(_tempDir, packageId.ToLowerInvariant(), version.ToLowerInvariant());
        Directory.CreateDirectory(packageDir);
        return packageDir;
    }

    private static void CreateDll(string directory, string assemblyName)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, assemblyName + ".dll"), [0x00]);
    }

    private static void CreateMarkerFile(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "_._"), "");
    }

    [Fact]
    public void GetPackagePath_ReturnsLowercasedPath()
    {
        var result = _cache.GetPackagePath("Newtonsoft.Json", "13.0.3");
        result.ShouldBe(Path.Combine(_tempDir, "newtonsoft.json", "13.0.3"));
    }

    [Fact]
    public void IsPackageCached_ReturnsTrueWhenExists()
    {
        CreatePackageLayout("TestPkg", "1.0.0");
        _cache.IsPackageCached("TestPkg", "1.0.0").ShouldBeTrue();
    }

    [Fact]
    public void IsPackageCached_ReturnsFalseWhenMissing()
    {
        _cache.IsPackageCached("NonExistent", "1.0.0").ShouldBeFalse();
    }

    [Fact]
    public void GetAssemblyPath_FindsInLib()
    {
        var pkg = CreatePackageLayout("TestPkg", "1.0.0");
        var libDir = Path.Combine(pkg, "lib", "net8.0");
        CreateDll(libDir, "TestPkg");

        var result = _cache.GetAssemblyPath("TestPkg", "1.0.0", "net8.0", "TestPkg", preferRef: false);

        result.ShouldNotBeNull();
        Path.GetFileName(result).ShouldBe("TestPkg.dll");
    }

    [Fact]
    public void GetAssemblyPath_PrefersRefWhenAvailable()
    {
        var pkg = CreatePackageLayout("TestPkg", "1.0.0");
        CreateDll(Path.Combine(pkg, "ref", "net8.0"), "TestPkg");
        CreateDll(Path.Combine(pkg, "lib", "net8.0"), "TestPkg");

        var result = _cache.GetAssemblyPath("TestPkg", "1.0.0", "net8.0", "TestPkg", preferRef: true);

        result.ShouldNotBeNull();
        result.ShouldContain(Path.Combine("ref", "net8.0"));
    }

    [Fact]
    public void GetAssemblyPath_FallsBackToLibWhenNoRef()
    {
        var pkg = CreatePackageLayout("TestPkg", "1.0.0");
        CreateDll(Path.Combine(pkg, "lib", "net8.0"), "TestPkg");

        var result = _cache.GetAssemblyPath("TestPkg", "1.0.0", "net8.0", "TestPkg", preferRef: true);

        result.ShouldNotBeNull();
        result.ShouldContain(Path.Combine("lib", "net8.0"));
    }

    [Fact]
    public void GetAssemblyPath_PreferRefFalse_IgnoresRefDirectory()
    {
        var pkg = CreatePackageLayout("TestPkg", "1.0.0");
        CreateDll(Path.Combine(pkg, "ref", "net8.0"), "TestPkg");

        var result = _cache.GetAssemblyPath("TestPkg", "1.0.0", "net8.0", "TestPkg", preferRef: false);

        result.ShouldBeNull();
    }

    [Fact]
    public void GetAssemblyPath_CaseInsensitiveAssemblyName()
    {
        var pkg = CreatePackageLayout("TestPkg", "1.0.0");
        CreateDll(Path.Combine(pkg, "lib", "net8.0"), "TestPkg");

        var result = _cache.GetAssemblyPath("TestPkg", "1.0.0", "net8.0", "testpkg", preferRef: false);

        result.ShouldNotBeNull();
    }

    [Fact]
    public void GetAssemblyPath_RuntimesFallbackWithMarkerFile()
    {
        var pkg = CreatePackageLayout("TestPkg", "1.0.0");
        var rid = RuntimeInformation.RuntimeIdentifier;
        CreateMarkerFile(Path.Combine(pkg, "lib", "net8.0"));
        CreateDll(Path.Combine(pkg, "runtimes", rid, "lib", "net8.0"), "TestPkg");

        var result = _cache.GetAssemblyPath("TestPkg", "1.0.0", "net8.0", "TestPkg", preferRef: false);

        result.ShouldNotBeNull();
        result.ShouldContain(Path.Combine("runtimes", rid));
    }

    [Fact]
    public void GetAssemblyPath_RuntimesFallbackWithEmptyLib()
    {
        var pkg = CreatePackageLayout("TestPkg", "1.0.0");
        var rid = RuntimeInformation.RuntimeIdentifier;
        Directory.CreateDirectory(Path.Combine(pkg, "lib", "net8.0"));
        CreateDll(Path.Combine(pkg, "runtimes", rid, "lib", "net8.0"), "TestPkg");

        var result = _cache.GetAssemblyPath("TestPkg", "1.0.0", "net8.0", "TestPkg", preferRef: false);

        result.ShouldNotBeNull();
        result.ShouldContain(Path.Combine("runtimes", rid));
    }

    [Fact]
    public void GetAssemblyPath_ReturnsNullWhenPackageNotCached()
    {
        var result = _cache.GetAssemblyPath("NonExistent", "1.0.0", "net8.0", "Foo", preferRef: false);
        result.ShouldBeNull();
    }

    [Fact]
    public void GetAvailableTfms_ScansLibAndRef()
    {
        var pkg = CreatePackageLayout("TestPkg", "1.0.0");
        CreateDll(Path.Combine(pkg, "lib", "net8.0"), "TestPkg");
        CreateDll(Path.Combine(pkg, "ref", "netstandard2.0"), "TestPkg");

        var result = _cache.GetAvailableTfms("TestPkg", "1.0.0");

        result.ShouldContain("net8.0");
        result.ShouldContain("netstandard2.0");
    }

    [Fact]
    public void GetAvailableTfms_DeduplicatesBetweenLibAndRef()
    {
        var pkg = CreatePackageLayout("TestPkg", "1.0.0");
        CreateDll(Path.Combine(pkg, "lib", "net8.0"), "TestPkg");
        CreateDll(Path.Combine(pkg, "ref", "net8.0"), "TestPkg");

        var result = _cache.GetAvailableTfms("TestPkg", "1.0.0");

        result.Count(t => t.Equals("net8.0", StringComparison.OrdinalIgnoreCase)).ShouldBe(1);
    }

    [Fact]
    public void GetAvailableTfms_ReturnsEmptyForMissingPackage()
    {
        var result = _cache.GetAvailableTfms("NonExistent", "1.0.0");
        result.ShouldBeEmpty();
    }

    [Fact]
    public void GetAssembliesForTfm_ListsAssemblyNames()
    {
        var pkg = CreatePackageLayout("TestPkg", "1.0.0");
        var libDir = Path.Combine(pkg, "lib", "net8.0");
        CreateDll(libDir, "Alpha");
        CreateDll(libDir, "Beta");

        var result = _cache.GetAssembliesForTfm("TestPkg", "1.0.0", "net8.0");

        result.ShouldBe(["Alpha", "Beta"]);
    }

    [Fact]
    public void GetAssembliesForTfm_ExcludesMarkerFiles()
    {
        var pkg = CreatePackageLayout("TestPkg", "1.0.0");
        var libDir = Path.Combine(pkg, "lib", "net8.0");
        CreateDll(libDir, "Alpha");
        CreateMarkerFile(libDir);

        var result = _cache.GetAssembliesForTfm("TestPkg", "1.0.0", "net8.0");

        result.ShouldBe(["Alpha"]);
        result.ShouldNotContain("_._");
    }

    [Fact]
    public void GetAssembliesForTfm_FallsBackToRuntimesWithMarkerOnly()
    {
        var pkg = CreatePackageLayout("TestPkg", "1.0.0");
        var rid = RuntimeInformation.RuntimeIdentifier;
        CreateMarkerFile(Path.Combine(pkg, "lib", "net8.0"));
        CreateDll(Path.Combine(pkg, "runtimes", rid, "lib", "net8.0"), "RuntimeAssembly");

        var result = _cache.GetAssembliesForTfm("TestPkg", "1.0.0", "net8.0");

        result.ShouldBe(["RuntimeAssembly"]);
    }

    [Fact]
    public void GetAssembliesForTfm_ReturnsEmptyForMissingTfm()
    {
        CreatePackageLayout("TestPkg", "1.0.0");

        var result = _cache.GetAssembliesForTfm("TestPkg", "1.0.0", "net99.0");

        result.ShouldBeEmpty();
    }
}
