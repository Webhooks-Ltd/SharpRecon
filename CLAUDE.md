# SharpRecon

.NET MCP server for NuGet package inspection, type introspection, and decompilation.

## Rules

- When adding or changing features, update `README.md` to reflect the current state
- When adding or changing features, update `CHANGELOG.md` under the `[Unreleased]` section
- Don't add comments to code unless absolutely necessary
- Read `openspec/project.md` for full project context, architecture, and conventions
- Read `openspec/specs/` for detailed requirements and scenarios before implementing
- Use `openspec status` to check current change progress

## Commit Convention

Use [Conventional Commits](https://www.conventionalcommits.org/):

| Prefix | When |
|---|---|
| `feat:` | New capability / public API |
| `fix:` | Bug fix |
| `docs:` | README, XML docs |
| `chore:` | CI, build, housekeeping |
| `refactor:` | Internal restructuring |
| `test:` | Tests only |
| `feat!:` / `fix!:` | Breaking change |

Lowercase after prefix, imperative mood, under 72 chars. Optional scope: `feat(inspection): add record detection`.

## Tech Stack

- Runtime: .NET 10.0
- MCP SDK: `ModelContextProtocol` 1.0.0 (stdio transport)
- Assembly inspection: `System.Reflection.MetadataLoadContext`
- Decompilation: `ICSharpCode.Decompiler`
- NuGet operations: `NuGet.Protocol` + `NuGet.Packaging`
- Tests: xUnit v3, Shouldly, NSubstitute, AutoFixture

## Project Structure

- `src/SharpRecon/NuGet/` — NuGet download service and tools
- `src/SharpRecon/Inspection/` — Type inspection, signature rendering, XML docs, and tools
- `src/SharpRecon/Decompilation/` — ICSharpCode.Decompiler wrapper and tools
- `src/SharpRecon/Infrastructure/` — Package cache, framework resolution, dependency resolution
- `tests/SharpRecon.Tests/` — Unit and integration tests

## Key Design Decisions

- Interfaces at I/O boundaries only: `INuGetService`, `IAssemblyInspector`, `IPackageCache`
- `TypeRenderer` is static/pure — no state, no I/O
- `MetadataLoadContext` and `CSharpDecompiler` created per-request, not cached
- Inspection prefers `ref/` assemblies; decompilation uses `lib/`
- Optional params on MCP tools must have `= null` defaults (MCP SDK uses C# defaults for required/optional)
- All tool methods return `CallToolResult` with `IsError = true` for errors — never let exceptions escape to stdout
