using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using SharpRecon.Decompilation;
using SharpRecon.Infrastructure;
using SharpRecon.Infrastructure.Resolution;
using SharpRecon.Inspection;
using SharpRecon.NuGet;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

PackageCache.AddPackageCache(builder.Services);
NuGetService.AddNuGetService(builder.Services);
XmlDocParser.AddXmlDocParser(builder.Services);

builder.Services.AddSingleton<FrameworkAssemblyResolver>();
builder.Services.AddSingleton<NuspecReader>();
builder.Services.AddSingleton<GlobalCacheAssemblyResolver>();
builder.Services.AddSingleton<NuGetDependencyResolver>();
builder.Services.AddSingleton<AssemblyPathResolver>();
builder.Services.AddSingleton<IAssemblyInspector, AssemblyInspector>();
builder.Services.AddSingleton<AssemblyDecompiler>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
