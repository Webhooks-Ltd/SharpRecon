## Why

AI coding agents need to explore the .NET ecosystem — discover types, read API signatures, understand documentation, and inspect implementations — without requiring a pre-existing project or solution context. Today this requires manually browsing docs.microsoft.com or decompiling with external tools. A .NET MCP server that can download NuGet packages on demand and inspect their assemblies gives agents (and developers) instant, structured access to any public .NET API.

## What Changes

- New stdio MCP server built on .NET 10.0 with the `ModelContextProtocol` SDK
- Tools to download NuGet packages and enumerate cached package contents (assemblies, TFMs)
- Tools to list, search, and inspect types within assemblies using `System.Reflection.MetadataLoadContext`, with XML doc comments included inline alongside signatures
- Tools to retrieve full C#-style member signatures with generic constraints, nullability, and XML doc comments for each overload
- Tools to decompile types and individual members back to C# source using `ICSharpCode.Decompiler`
- Three-tier assembly dependency resolution (framework targeting packs, NuGet global cache, NuGet Protocol download) so inspected assemblies have their transitive dependencies available
- TFM-aware filtering so agents can reason about platform-specific APIs
- Single project organized by capability (`NuGet/`, `Inspection/`, `Decompilation/`, `Infrastructure/`) with thin MCP tool adapters over concrete service classes

## Capabilities

### New Capabilities
- `nuget-operations`: Download NuGet packages to global cache, resolve latest versions, list available TFMs and assemblies within a cached package
- `type-inspection`: List, search, and get detailed signatures for public types and their members across assemblies in a cached NuGet package — including XML doc comments inline — with optional TFM, namespace, and assembly filtering. Decompile types and individual members back to C# source.
- `assembly-resolution`: Three-tier transitive dependency resolution (framework targeting packs, NuGet global cache, NuGet Protocol download) so that `MetadataLoadContext` and the decompiler have all referenced assemblies available. Results cached per (packageId, version, TFM) with configurable depth limit.

### Modified Capabilities

## Impact

- New standalone .NET 10.0 console project (no existing code affected)
- Dependencies: `ModelContextProtocol` 1.0.0, `Microsoft.Extensions.Hosting`, `NuGet.Protocol`, `NuGet.Packaging`, `ICSharpCode.Decompiler`
- Reads from and writes to the NuGet global packages cache (`~/.nuget/packages`)
- Uses `System.Reflection.MetadataLoadContext` for safe, non-executing assembly inspection
- Uses `ICSharpCode.Decompiler` (ILSpy engine) for C# decompilation
- Discovers installed .NET targeting packs on disk for framework reference resolution
- May download transitive NuGet dependencies on demand when not present in global cache
- Exposes MCP tools over stdio transport
