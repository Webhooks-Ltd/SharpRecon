# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.5]

### Added

- `nuget_search` — Search NuGet.org for packages by query, returning package IDs, versions, descriptions, and download counts
- `investigate_package` prompt — Guides step-by-step investigation of a NuGet package from download through decompilation
- `compare_versions` prompt — Guides comparison of a package's public API surface between two versions

### Improved

- All tool descriptions refined for better LLM tool selection and disambiguation
- `nuget_download` description now links back to `nuget_search` when package ID is unknown
- `assembly_list` clarifies it is not required before `type_search`
- `type_list` / `type_search` descriptions now emphasize input requirements as the selection criterion
- `type_detail` / `member_detail` descriptions include "Fast — no decompilation" performance signal
- `parameterTypes` descriptions include common CLR alias mappings (string→System.String, etc.)
- `assemblyName` hint descriptions clarify consequence of omission
- `type_search` empty results message now suggests actionable next steps
- `plugin.json` keywords expanded for discoverability

## [0.1.4]

### Added

- `nuget_download` — Download NuGet packages with exact, wildcard, or latest version resolution
- `assembly_list` — List assemblies in a cached package, grouped by TFM
- `type_list` — List public types in an assembly, grouped by namespace
- `type_search` — Search types by name across assemblies
- `type_detail` — Full type declaration, XML docs, and member signatures
- `member_detail` — Overload signatures and XML docs for a specific member
- `decompile_type` — Decompile a type to C# source
- `decompile_member` — Decompile a single member to C# source
- Three-tier assembly dependency resolution (targeting packs, NuGet global cache, NuGet Protocol download)
- `ref/` assembly preference for inspection, `lib/` for decompilation
- XML doc comment parsing from sidecar `.xml` files
- Full C# signature rendering with nullability, generics, constraints, default values
- Record detection heuristic (best-effort)
- Cross-platform Node.js launcher (`launcher.js`) with shadow-copy, auto-download from GitHub releases, and auto-update
- Claude Code plugin support (`.claude-plugin/plugin.json` + `.mcp.json`)
- GitHub Actions release workflow for cross-platform self-contained builds (win-x64, linux-x64, osx-x64, osx-arm64)

### Fixed

- Assembly loading error when inspecting framework targeting pack packages (e.g. `Microsoft.NETCore.App.Ref`) where the same assembly appeared from both package and framework paths
- Self-contained release binaries now include runtime DLLs on disk so `MetadataLoadContext` works on machines without the .NET SDK
