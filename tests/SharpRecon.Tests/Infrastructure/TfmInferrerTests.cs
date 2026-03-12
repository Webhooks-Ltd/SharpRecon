using SharpRecon.Infrastructure;
using Shouldly;
using Xunit;

namespace SharpRecon.Tests.Infrastructure;

public sealed class TfmInferrerTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    private string CreateTempJsonFile(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.runtimeconfig.json");
        File.WriteAllText(path, json);
        _tempFiles.Add(path);
        return path;
    }

    [Theory]
    [InlineData(".NETCoreApp,Version=v10.0", "net10.0")]
    [InlineData(".NETCoreApp,Version=v8.0", "net8.0")]
    [InlineData(".NETCoreApp,Version=v5.0", "net5.0")]
    [InlineData(".NETCoreApp,Version=v3.1", "netcoreapp3.1")]
    [InlineData(".NETCoreApp,Version=v2.1", "netcoreapp2.1")]
    [InlineData(".NETFramework,Version=v4.8", "net48")]
    [InlineData(".NETFramework,Version=v4.7.2", "net472")]
    [InlineData(".NETStandard,Version=v2.0", "netstandard2.0")]
    [InlineData(".NETStandard,Version=v2.1", "netstandard2.1")]
    public void ParseFrameworkName_KnownFrameworks_ReturnsExpectedTfm(string input, string expected)
    {
        TfmInferrer.ParseFrameworkName(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("InvalidFramework")]
    public void ParseFrameworkName_InvalidInput_ReturnsNull(string input)
    {
        TfmInferrer.ParseFrameworkName(input).ShouldBeNull();
    }

    [Fact]
    public void ParseFrameworkName_UnknownIdentifier_ReturnsNull()
    {
        TfmInferrer.ParseFrameworkName(".NETMicro,Version=v4.0").ShouldBeNull();
    }

    [Fact]
    public void InferTfm_NonExistentFile_ReturnsNull()
    {
        TfmInferrer.InferTfm(@"C:\nonexistent\fake.dll").ShouldBeNull();
    }

    [Fact]
    public void InferTfmForDirectory_TestOutputDirectory_ReturnsValidTfm()
    {
        var directory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var assemblies = Directory.EnumerateFiles(directory, "*.dll").ToList();

        var tfm = TfmInferrer.InferTfmForDirectory(directory, assemblies);

        tfm.ShouldNotBeNullOrWhiteSpace();
        tfm.ShouldStartWith("net");
    }

    [Fact]
    public void InferTfmFromRuntimeConfig_SingleFramework_ReturnsCorrectTfm()
    {
        var json = """
        {
          "runtimeOptions": {
            "framework": {
              "name": "Microsoft.NETCore.App",
              "version": "8.0.0"
            }
          }
        }
        """;
        var path = CreateTempJsonFile(json);

        TfmInferrer.InferTfmFromRuntimeConfig(path).ShouldBe("net8.0");
    }

    [Fact]
    public void InferTfmFromRuntimeConfig_FrameworksArray_ReturnsCorrectTfm()
    {
        var json = """
        {
          "runtimeOptions": {
            "frameworks": [
              {
                "name": "Microsoft.NETCore.App",
                "version": "10.0.0"
              },
              {
                "name": "Microsoft.AspNetCore.App",
                "version": "10.0.0"
              }
            ]
          }
        }
        """;
        var path = CreateTempJsonFile(json);

        TfmInferrer.InferTfmFromRuntimeConfig(path).ShouldBe("net10.0");
    }

    [Fact]
    public void InferTfmFromRuntimeConfig_AspNetCoreFramework_ReturnsCorrectTfm()
    {
        var json = """
        {
          "runtimeOptions": {
            "framework": {
              "name": "Microsoft.AspNetCore.App",
              "version": "6.0.0"
            }
          }
        }
        """;
        var path = CreateTempJsonFile(json);

        TfmInferrer.InferTfmFromRuntimeConfig(path).ShouldBe("net6.0");
    }

    [Fact]
    public void InferTfmFromRuntimeConfig_OldCoreVersion_ReturnsNetCoreAppTfm()
    {
        var json = """
        {
          "runtimeOptions": {
            "framework": {
              "name": "Microsoft.NETCore.App",
              "version": "3.1.0"
            }
          }
        }
        """;
        var path = CreateTempJsonFile(json);

        TfmInferrer.InferTfmFromRuntimeConfig(path).ShouldBe("netcoreapp3.1");
    }

    [Fact]
    public void InferTfmFromRuntimeConfig_NonExistentFile_ReturnsNull()
    {
        TfmInferrer.InferTfmFromRuntimeConfig(@"C:\nonexistent\fake.runtimeconfig.json").ShouldBeNull();
    }

    [Fact]
    public void InferTfmFromRuntimeConfig_InvalidJson_ReturnsNull()
    {
        var path = CreateTempJsonFile("not valid json {{{");

        TfmInferrer.InferTfmFromRuntimeConfig(path).ShouldBeNull();
    }

    [Fact]
    public void InferTfmFromRuntimeConfig_MissingRuntimeOptions_ReturnsNull()
    {
        var path = CreateTempJsonFile("{}");

        TfmInferrer.InferTfmFromRuntimeConfig(path).ShouldBeNull();
    }

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            try { File.Delete(path); } catch { }
        }
    }
}
