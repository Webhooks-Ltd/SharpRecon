## Why

SharpRecon's decompilation and inspection tools currently require a NuGet package context — users must first `nuget_download` a package before they can inspect or decompile types. This prevents using SharpRecon against local .NET assemblies (project build output, third-party DLLs, installed tools). Supporting local assemblies makes SharpRecon useful for a much broader set of investigation workflows: debugging your own builds, auditing vendored binaries, or exploring assemblies that aren't published on NuGet.

## What Changes

- New MCP tool `local_load` that accepts a file path (single `.dll`/`.exe`) or directory path and registers assemblies for inspection/decompilation. Returns synthetic identifiers (`packageId = "local:{name}"`, `version = "local"`) that feed into existing tools unchanged — mirroring the `nuget_download` → inspect/decompile workflow.
- **No signature changes to existing tools.** Existing inspection and decompilation tools work with locally-loaded assemblies transparently via the synthetic identifiers from `local_load`. The internal routing uses an `AssemblySource` discriminated union (`NuGetSource` vs `LocalSource`) to branch resolution logic.
- Assembly resolution adapts to resolve dependencies for local assemblies: sibling DLLs in the same directory, framework assemblies inferred from `TargetFrameworkAttribute`, and `.deps.json` if present.
- XML documentation probing for local assemblies: `{assemblyName}.xml` in the same directory as the DLL.
- `assembly_list` works with local loads — lists all assemblies registered from the directory.

### Unsupported Scenarios (explicit errors)

- **Single-file published assemblies**: Detected via bundle marker; returns clear "not supported" error instead of a cryptic PEFile failure.
- **Native DLLs**: Validated via CLR header check before any operations.
- **Mixed-mode assemblies (C++/CLI)**: Managed portions are readable; native methods decompile to empty bodies. Noted as a known limitation in tool output.

## Capabilities

### New Capabilities

- `local-assembly-loading`: Loading local .NET assemblies by file path or directory and making them available to all existing inspection and decompilation tools via synthetic identifiers and an `AssemblySource` abstraction

### Modified Capabilities

- `assembly-resolution`: Dependency resolution must handle local assemblies where there is no NuGet package graph. Resolution order: (1) `.deps.json` if present alongside the assembly, (2) `TargetFrameworkAttribute` from assembly metadata for framework assembly resolution, (3) sibling directory probing, (4) graceful fallback with unresolved dependency warnings. Local resolution is a parallel path — `IPackageCache` is not extended.
- `type-inspection`: Inspection and decompilation tools recognise synthetic `local:*` package IDs and route to local resolution. XML docs probed from `{assemblyName}.xml` in the assembly's directory. PDBs in the same directory are used automatically by ICSharpCode.Decompiler if present.

## Impact

- **Tools**: Tool count increases by 1 (`local_load`, bringing total to 10). No parameter changes on existing tools. `nuget_search` and `nuget_download` remain NuGet-only; all other tools work with both sources. `assembly_list` description updated to cover both modes.
- **Infrastructure**: New `AssemblySource` type hierarchy (`NuGetSource` / `LocalSource`) replaces stringly-typed package params in `AssemblyDecompiler` and `AssemblyInspector` internals. `AssemblyPathResolver` gains a local resolution path that skips NuGet dependency resolution entirely.
- **Path handling**: Paths normalised via `Path.GetFullPath`. Relative paths resolved against CWD. Must point to existing files/directories. UNC paths and symlinks accepted (OS-level resolution).
- **No new dependencies**: ICSharpCode.Decompiler, MetadataLoadContext, and `Microsoft.Extensions.DependencyModel` (in shared framework) already handle arbitrary file paths.
- **Security**: File path access is constrained to the MCP server's host filesystem — same trust model as any stdio MCP server.
