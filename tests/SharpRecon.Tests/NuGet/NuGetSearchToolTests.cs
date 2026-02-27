using NSubstitute;
using SharpRecon.NuGet;
using Shouldly;
using Xunit;

namespace SharpRecon.Tests.NuGet;

public sealed class NuGetSearchToolTests
{
    private readonly INuGetService _service = Substitute.For<INuGetService>();

    [Fact]
    public async Task SearchAsync_ReturnsFormattedMarkdown()
    {
        _service.SearchAsync("json", 10, Arg.Any<CancellationToken>())
            .Returns([
                new NuGetSearchResult("Newtonsoft.Json", "13.0.3", "Popular JSON framework", 2_100_000_000, true),
                new NuGetSearchResult("System.Text.Json", "9.0.4", "High-performance JSON APIs", 890_000_000, true),
            ]);

        var result = await NuGetSearchTool.SearchAsync("json", _service, CancellationToken.None);

        result.IsError.ShouldNotBe(true);
        var text = ((ModelContextProtocol.Protocol.TextContentBlock)result.Content[0]).Text!;
        text.ShouldContain("Newtonsoft.Json");
        text.ShouldContain("13.0.3");
        text.ShouldContain("[verified]");
        text.ShouldContain("2.1B");
        text.ShouldContain("System.Text.Json");
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsError()
    {
        var result = await NuGetSearchTool.SearchAsync("  ", _service, CancellationToken.None);

        result.IsError.ShouldBe(true);
        ((ModelContextProtocol.Protocol.TextContentBlock)result.Content[0]).Text.ShouldContain("must not be empty");
    }

    [Fact]
    public async Task SearchAsync_NoResults_ReturnsMessage()
    {
        _service.SearchAsync("xyznonexistent", 10, Arg.Any<CancellationToken>())
            .Returns(new List<NuGetSearchResult>());

        var result = await NuGetSearchTool.SearchAsync("xyznonexistent", _service, CancellationToken.None);

        result.IsError.ShouldNotBe(true);
        ((ModelContextProtocol.Protocol.TextContentBlock)result.Content[0]).Text.ShouldContain("No packages found");
    }

    [Fact]
    public async Task SearchAsync_TakeCappedAt20()
    {
        _service.SearchAsync("test", 20, Arg.Any<CancellationToken>())
            .Returns(new List<NuGetSearchResult>());

        await NuGetSearchTool.SearchAsync("test", _service, CancellationToken.None, take: 50);

        await _service.Received(1).SearchAsync("test", 20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_DefaultTakeIs10()
    {
        _service.SearchAsync("test", 10, Arg.Any<CancellationToken>())
            .Returns(new List<NuGetSearchResult>());

        await NuGetSearchTool.SearchAsync("test", _service, CancellationToken.None);

        await _service.Received(1).SearchAsync("test", 10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_ServiceThrows_ReturnsError()
    {
        _service.When(x => x.SearchAsync("fail", 10, Arg.Any<CancellationToken>()))
            .Do(_ => throw new HttpRequestException("Network error"));

        var result = await NuGetSearchTool.SearchAsync("fail", _service, CancellationToken.None);

        result.IsError.ShouldBe(true);
        ((ModelContextProtocol.Protocol.TextContentBlock)result.Content[0]).Text.ShouldContain("Network error");
    }
}
