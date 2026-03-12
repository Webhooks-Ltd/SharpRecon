<p align="center">
  <img src="logo.svg" alt="SharpRecon logo" width="128" />
</p>

# SharpRecon

[![CI](https://github.com/Webhooks-Ltd/SharpRecon/actions/workflows/ci.yml/badge.svg)](https://github.com/Webhooks-Ltd/SharpRecon/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![MCP](https://img.shields.io/badge/MCP-stdio-green.svg)](https://modelcontextprotocol.io/)

An MCP server that gives AI agents deep visibility into the .NET ecosystem. Search NuGet, browse assemblies, read type signatures with XML docs, and decompile to C# source — all without leaving the conversation.

## Why SharpRecon?

When an AI agent needs to understand a .NET library — how to call an API, what overloads exist, what changed between versions — it typically has to rely on training data that may be outdated or incomplete. SharpRecon lets the agent go straight to the source: download the actual package, inspect its real type signatures and documentation, and decompile the implementation when needed. Every answer comes from the package itself, not from memory.

## Install

### Claude Code plugin (recommended)

```
/plugin marketplace add Webhooks-Ltd/claude-plugins
/plugin install sharp-recon
```

The plugin downloads the correct pre-built binary for your platform automatically on first use, and updates itself when new releases are available.

### VS Code / Cursor

Add to `.vscode/mcp.json` in your workspace:

```json
{
  "servers": {
    "sharp-recon": {
      "type": "stdio",
      "command": "node",
      "args": ["/path/to/SharpRecon/launcher.js"]
    }
  }
}
```

### Any MCP client

SharpRecon speaks stdio MCP. Point your client at the launcher:

```bash
node /path/to/SharpRecon/launcher.js
```

## Tools

Ten tools arranged as a progressive drill-down pipeline — start broad, narrow as needed:

| Tool | Purpose |
|------|---------|
| `nuget_search` | Find packages on NuGet.org by keyword |
| `nuget_download` | Download a package and return version, TFMs, and health (deprecation, vulnerabilities, publish date) |
| `local_load` | Load a local .NET assembly (.dll/.exe) or directory for inspection and decompilation |
| `assembly_list` | List assemblies in a package or local load, grouped by target framework |
| `type_list` | List all public types in an assembly, grouped by namespace |
| `type_search` | Search for types by name across all assemblies in a package or local load |
| `type_detail` | Get the full type declaration: XML docs, base types, and all member signatures |
| `member_detail` | Get all overload signatures and XML docs for a specific member |
| `decompile_type` | Decompile a type to C# source code |
| `decompile_member` | Decompile a single member to C# source code |

**Key conventions:**
- Inspection and decompilation tools work with both NuGet packages (`nuget_download`) and local assemblies (`local_load`).
- All tools after `nuget_download` / `local_load` require the identifiers they return.
- Assembly names omit the `.dll` extension.
- Type names must be fully qualified (e.g. `Newtonsoft.Json.JsonConvert`).
- When `tfm` is omitted, the highest available framework is selected automatically.
- `type_detail` and `member_detail` are fast (no decompilation). Use `decompile_*` only when you need implementation source.

## Skills

When installed as a Claude Code plugin, two slash commands provide guided workflows:

| Skill | Description | Example |
|-------|-------------|---------|
| `/investigate-package` | Step-by-step investigation of a NuGet package — download, browse types, read signatures, decompile | `/investigate-package Newtonsoft.Json` |
| `/compare-versions` | Compare the public API surface between two versions to find breaking changes and new APIs | `/compare-versions Newtonsoft.Json 12.0.3 13.0.3` |

## Typical workflow

An agent drills down from package search to source through progressively narrower tools:

```
nuget_search             Find a JSON serialization library
  nuget_download         Download Newtonsoft.Json 13.*
  assembly_list          What assemblies and TFMs does it contain?
    type_search          Find types matching "JsonConvert"
      type_detail        Full declaration and member signatures
        member_detail    All overloads of SerializeObject with docs
        decompile_member Source of SerializeObject(object)
```

For local assemblies, the same pipeline starts with `local_load` instead:

```
local_load               Load a build output directory or single DLL
  assembly_list          What assemblies were loaded?
    type_search          Find types matching "UserService"
      type_detail        Full declaration and member signatures
        decompile_type   Source of the entire type
```

## Launcher

The `launcher.js` script handles binary management so you don't have to:

- **Auto-download** — On first run, fetches the correct release binary for your platform (win-x64, linux-x64, osx-x64, osx-arm64) from GitHub Releases.
- **Version-pinned** — Downloads the exact release matching the plugin version, and upgrades when the plugin updates.
- **Shadow-copy** — Copies binaries to a temp directory before launch, preventing file locks during rebuilds. Stale copies are cleaned up automatically.

## Building from source

Requires [.NET 10.0 SDK](https://dotnet.microsoft.com/download).

```bash
# Build
dotnet build

# Run tests
dotnet test

# Publish (required before the launcher can use local binaries)
dotnet publish src/SharpRecon/SharpRecon.csproj -o src/SharpRecon/bin/publish
```

When developing locally, republish after code changes — the launcher reads from the publish directory, not build output.
