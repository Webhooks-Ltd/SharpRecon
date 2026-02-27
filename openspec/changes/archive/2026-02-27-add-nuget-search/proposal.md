## Why

Agents currently must know the exact NuGet package ID to start the inspection workflow. When users ask exploratory questions ("find a logging library for .NET") or need disambiguation ("is it Microsoft.Extensions.Logging or Microsoft.Extensions.Logging.Abstractions?"), the agent has no tool to discover package IDs and is stuck.

## What Changes

- Add `nuget_search` tool that queries the NuGet V3 Search API
- Returns package ID, latest stable version, truncated description, download count, and verified owner flag
- Capped at 10 results by default (max 20), no pagination
- Single HTTP call to `https://azuresearch-usnc.nuget.org/query`, no new dependencies

## Capabilities

### New Capabilities
- `nuget-search`: NuGet package discovery via search query, returning package IDs and metadata for agents to proceed with `nuget_download`

### Modified Capabilities
- `nuget-operations`: Adding search as a new operation alongside the existing download capability

## Impact

- New file: `src/SharpRecon/NuGet/NuGetSearchTool.cs`
- Modified: `INuGetService` / `NuGetService` to add search method
- No new package dependencies (uses existing `HttpClient` or NuGet.Protocol)
- No breaking changes to existing tools
