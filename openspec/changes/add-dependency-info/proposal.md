## Why

When an LLM downloads a NuGet package, it has no visibility into what that package depends on. Dependencies affect compatibility, security posture, and bundle size — but the agent can't see them without leaving the MCP workflow. The .nuspec file in the local cache already contains this data, so surfacing it requires zero network calls.

## What Changes

- `nuget_download` output gains dependency counts per TFM in the existing TFM list (e.g. `net8.0 (3 deps)`)
- `assembly_list` output gains a "Dependencies" section per TFM showing package IDs and version ranges
- Large dependency lists (>15 per TFM) are capped with a "filter by TFM to see all" hint
- No new tools — dependency data is progressive disclosure along the existing drill-down pipeline

## Capabilities

### New Capabilities

(none — this enriches existing tools)

### Modified Capabilities

- `nuget-operations`: `nuget_download` response now includes dependency counts per TFM
- `assembly-resolution`: `assembly_list` response now includes NuGet dependency groups per TFM alongside assembly names

## Impact

- `src/SharpRecon/NuGet/NuGetDownloadTool.cs` — format dep counts in TFM list
- `src/SharpRecon/Inspection/AssemblyListTool.cs` — read and format dependencies from .nuspec
- `src/SharpRecon/Infrastructure/Resolution/NuspecReader.cs` — may need a method to get dependency counts or all dependency groups (existing class already reads deps per-TFM)
- Tool descriptions for `nuget_download` and `assembly_list` updated
