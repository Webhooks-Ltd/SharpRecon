# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
- Shadow-copy launcher script (`launcher.sh`) for file-locking prevention
- Claude Code plugin support (`.claude-plugin/plugin.json` + `.mcp.json`)
- Auto-download of pre-built binaries from GitHub releases on first launch
- GitHub Actions release workflow for cross-platform self-contained builds (win-x64, linux-x64, osx-x64, osx-arm64)
- Bash launcher (`launcher.sh`) — no PowerShell dependency

### Fixed

- Assembly loading error when inspecting framework targeting pack packages (e.g. `Microsoft.NETCore.App.Ref`) where the same assembly appeared from both package and framework paths
- Self-contained release binaries now include runtime DLLs on disk so `MetadataLoadContext` works on machines without the .NET SDK
