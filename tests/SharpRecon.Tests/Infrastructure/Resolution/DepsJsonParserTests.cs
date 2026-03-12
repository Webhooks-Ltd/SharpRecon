using SharpRecon.Infrastructure.Resolution;
using Shouldly;
using Xunit;

namespace SharpRecon.Tests.Infrastructure.Resolution;

public sealed class DepsJsonParserTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"DepsJsonParserTests_{Guid.NewGuid()}");

    public DepsJsonParserTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void ResolveAssemblyPaths_ProjectReference_ResolvesToAssemblyDirectory()
    {
        var dllPath = Path.Combine(_tempDir, "MyApp.dll");
        File.WriteAllBytes(dllPath, [0]);

        var depsJson = """
        {
          "runtimeTarget": { "name": ".NETCoreApp,Version=v10.0" },
          "targets": {
            ".NETCoreApp,Version=v10.0": {
              "MyApp/1.0.0": {
                "runtime": { "MyApp.dll": {} }
              }
            }
          },
          "libraries": {
            "MyApp/1.0.0": { "type": "project", "path": "MyApp/1.0.0" }
          }
        }
        """;
        var depsJsonPath = Path.Combine(_tempDir, "MyApp.deps.json");
        File.WriteAllText(depsJsonPath, depsJson);

        var result = DepsJsonParser.ResolveAssemblyPaths(depsJsonPath, _tempDir);

        result.ShouldNotBeEmpty();
        result.ShouldContain(dllPath);
    }

    [Fact]
    public void ResolveAssemblyPaths_PackageReference_ResolvesToNuGetCache()
    {
        var nugetPackagesPath = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

        var packageDir = Path.Combine(nugetPackagesPath, "newtonsoft.json", "13.0.3", "lib", "net6.0");
        var packageDll = Path.Combine(packageDir, "Newtonsoft.Json.dll");

        if (!File.Exists(packageDll))
            return;

        var depsJson = """
        {
          "runtimeTarget": { "name": ".NETCoreApp,Version=v10.0" },
          "targets": {
            ".NETCoreApp,Version=v10.0": {
              "Newtonsoft.Json/13.0.3": {
                "runtime": { "lib/net6.0/Newtonsoft.Json.dll": {} }
              }
            }
          },
          "libraries": {
            "Newtonsoft.Json/13.0.3": { "type": "package", "path": "newtonsoft.json/13.0.3" }
          }
        }
        """;
        var depsJsonPath = Path.Combine(_tempDir, "Test.deps.json");
        File.WriteAllText(depsJsonPath, depsJson);

        var result = DepsJsonParser.ResolveAssemblyPaths(depsJsonPath, _tempDir);

        result.ShouldNotBeEmpty();
        result.ShouldContain(packageDll);
    }

    [Fact]
    public void ResolveAssemblyPaths_NonExistentDepsJson_ReturnsEmptyList()
    {
        var result = DepsJsonParser.ResolveAssemblyPaths(
            Path.Combine(_tempDir, "nonexistent.deps.json"), _tempDir);

        result.ShouldBeEmpty();
    }

    [Fact]
    public void ResolveAssemblyPaths_InvalidJson_ReturnsEmptyList()
    {
        var depsJsonPath = Path.Combine(_tempDir, "invalid.deps.json");
        File.WriteAllText(depsJsonPath, "not valid json {{{");

        var result = DepsJsonParser.ResolveAssemblyPaths(depsJsonPath, _tempDir);

        result.ShouldBeEmpty();
    }

    [Fact]
    public void ResolveAssemblyPaths_NoRuntimeEntries_ReturnsEmptyList()
    {
        var depsJson = """
        {
          "runtimeTarget": { "name": ".NETCoreApp,Version=v10.0" },
          "targets": {
            ".NETCoreApp,Version=v10.0": {
              "MyApp/1.0.0": {}
            }
          },
          "libraries": {
            "MyApp/1.0.0": { "type": "project", "path": "MyApp/1.0.0" }
          }
        }
        """;
        var depsJsonPath = Path.Combine(_tempDir, "no-runtime.deps.json");
        File.WriteAllText(depsJsonPath, depsJson);

        var result = DepsJsonParser.ResolveAssemblyPaths(depsJsonPath, _tempDir);

        result.ShouldBeEmpty();
    }

    [Fact]
    public void ResolveAssemblyPaths_MixedProjectAndPackage_ResolvesLocalFirst()
    {
        var appDll = Path.Combine(_tempDir, "MyApp.dll");
        var libDll = Path.Combine(_tempDir, "MyLib.dll");
        File.WriteAllBytes(appDll, [0]);
        File.WriteAllBytes(libDll, [0]);

        var depsJson = """
        {
          "runtimeTarget": { "name": ".NETCoreApp,Version=v8.0" },
          "targets": {
            ".NETCoreApp,Version=v8.0": {
              "MyApp/1.0.0": {
                "runtime": { "MyApp.dll": {} }
              },
              "MyLib/1.0.0": {
                "runtime": { "MyLib.dll": {} }
              }
            }
          },
          "libraries": {
            "MyApp/1.0.0": { "type": "project", "path": "MyApp/1.0.0" },
            "MyLib/1.0.0": { "type": "project", "path": "MyLib/1.0.0" }
          }
        }
        """;
        var depsJsonPath = Path.Combine(_tempDir, "MyApp.deps.json");
        File.WriteAllText(depsJsonPath, depsJson);

        var result = DepsJsonParser.ResolveAssemblyPaths(depsJsonPath, _tempDir);

        result.Count.ShouldBe(2);
        result.ShouldContain(appDll);
        result.ShouldContain(libDll);
    }

    [Fact]
    public void ResolveAssemblyPaths_PackageDllNotOnDisk_SkipsIt()
    {
        var depsJson = """
        {
          "runtimeTarget": { "name": ".NETCoreApp,Version=v10.0" },
          "targets": {
            ".NETCoreApp,Version=v10.0": {
              "FakePackage/99.0.0": {
                "runtime": { "lib/net10.0/FakePackage.dll": {} }
              }
            }
          },
          "libraries": {
            "FakePackage/99.0.0": { "type": "package", "path": "fakepackage/99.0.0" }
          }
        }
        """;
        var depsJsonPath = Path.Combine(_tempDir, "test.deps.json");
        File.WriteAllText(depsJsonPath, depsJson);

        var result = DepsJsonParser.ResolveAssemblyPaths(depsJsonPath, _tempDir);

        result.ShouldBeEmpty();
    }

    [Fact]
    public void ResolveAssemblyPaths_MissingRuntimeTarget_ReturnsEmptyList()
    {
        var depsJson = """
        {
          "targets": {
            ".NETCoreApp,Version=v10.0": {
              "MyApp/1.0.0": {
                "runtime": { "MyApp.dll": {} }
              }
            }
          }
        }
        """;
        var depsJsonPath = Path.Combine(_tempDir, "no-target.deps.json");
        File.WriteAllText(depsJsonPath, depsJson);

        var result = DepsJsonParser.ResolveAssemblyPaths(depsJsonPath, _tempDir);

        result.ShouldBeEmpty();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
