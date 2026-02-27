<p align="center">
  <img src="logo.svg" alt="SharpRecon logo" width="128" />
</p>

# SharpRecon

[![CI](https://github.com/Webhooks-Ltd/SharpRecon/actions/workflows/ci.yml/badge.svg)](https://github.com/Webhooks-Ltd/SharpRecon/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![MCP](https://img.shields.io/badge/MCP-stdio-green.svg)](https://modelcontextprotocol.io/)

A .NET 10.0 stdio MCP server that lets AI agents inspect the .NET ecosystem -- download NuGet packages, browse types, read signatures with XML docs, and decompile source.

## Quick start

### Build and publish

```bash
dotnet publish src/SharpRecon/SharpRecon.csproj -c Release -o src/SharpRecon/bin/publish
```

### Install as Claude Code plugin

```
/plugin marketplace add Webhooks-Ltd/claude-plugins
/plugin install sharp-recon
```

The launcher automatically downloads the correct pre-built binary for your platform on first use.

### Or add to Claude Code manually

```bash
claude mcp add --scope user sharp-recon -- bash "/path/to/SharpRecon/launcher.sh"
```

### Add to VS Code / Cursor

Create `.vscode/mcp.json` in your workspace:

```json
{
  "servers": {
    "sharp-recon": {
      "type": "stdio",
      "command": "bash",
      "args": ["/path/to/SharpRecon/launcher.sh"]
    }
  }
}
```

## Tools

| Tool | Description | Key parameters |
|------|-------------|----------------|
| `nuget_search` | Search NuGet.org for packages by query | `query`, `take` (1-20, default 10) |
| `nuget_download` | Download a NuGet package to the local cache | `packageId`, `version` (exact, wildcard, or omit for latest) |
| `assembly_list` | List assemblies in a cached package, grouped by TFM | `packageId`, `version`, `tfm` (optional) |
| `type_list` | List all public types in an assembly, grouped by namespace | `packageId`, `version`, `assemblyName`, `ns` (optional) |
| `type_search` | Search types by name across all assemblies in a package | `packageId`, `version`, `query` |
| `type_detail` | Full type declaration with XML docs and member signatures | `packageId`, `version`, `typeName` |
| `member_detail` | All overload signatures and XML docs for a single member | `packageId`, `version`, `typeName`, `memberName` |
| `decompile_type` | Decompile a type to C# source | `packageId`, `version`, `typeName`, `maxLength` (optional) |
| `decompile_member` | Decompile a single member to C# source | `packageId`, `version`, `typeName`, `memberName`, `parameterTypes` (optional) |

All inspection and decompilation tools require the **exact version** returned by `nuget_download`. Assembly names are specified without the `.dll` extension. When `tfm` is omitted, the server auto-selects the highest available (`net*` > `netstandard*` > `netcoreapp*`).

## Typical workflow

An agent drills down from package to source through progressively narrower tools:

```
nuget_search            -- "Find a JSON serialization library"
  nuget_download        -- "Get me Newtonsoft.Json 13.*"
  assembly_list         -- "What assemblies and TFMs does it contain?"
    type_search         -- "Find types matching 'JsonConvert'"
      type_detail       -- "Show me the full declaration and member signatures"
        member_detail   -- "Show all overloads of SerializeObject with XML docs"
        decompile_member -- "Show me the source of SerializeObject(object)"
```

`type_detail` returns signatures and XML docs without decompilation overhead. Use `decompile_type` or `decompile_member` only when the agent needs implementation source.

## Building from source

Requires .NET 10.0 SDK.

```bash
dotnet build
dotnet run --project tests/SharpRecon.Tests
```

## Shadow-copy launcher

The `launcher.sh` script copies the published binaries to a temporary directory before running the server. This prevents file-locking conflicts when you rebuild the project while the MCP server is still running. Stale shadow copies older than one hour are cleaned up automatically on launch.

If no published binaries are found, the launcher automatically downloads the latest release from GitHub for your platform (win-x64, linux-x64, osx-x64, osx-arm64).

When developing from source, **republish** before restarting the MCP server — the launcher reads from the publish directory, not the build output:

```bash
dotnet publish src/SharpRecon/SharpRecon.csproj -o src/SharpRecon/bin/publish
```

## License

MIT
