## Context

SharpRecon's current workflow requires agents to know exact NuGet package IDs. The NuGet V3 Search API provides free, unauthenticated search at `https://azuresearch-usnc.nuget.org/query`. We need a single tool that bridges the discovery gap without encouraging tangent browsing.

## Goals / Non-Goals

**Goals:**
- Let agents discover package IDs via free-text search
- Return just enough metadata to pick a package and move to `nuget_download`
- Keep token footprint minimal (10 results ~600 tokens)

**Non-Goals:**
- Version listing / version browsing (existing `nuget_download` handles this)
- Pagination (if the answer isn't in top 20, refine the query)
- Package metadata beyond what's needed for selection (tags, authors, dependencies)

## Decisions

### Use NuGet V3 Search API directly via HttpClient
The NuGet.Protocol package has search capabilities, but a single `HttpClient.GetAsync` to the Search API is simpler, faster, and avoids the `SourceRepository` ceremony. The search endpoint returns JSON with all fields we need. We already have `HttpClient` available via DI (`IHttpClientFactory` or direct).

**Alternative**: Use `PackageSearchResource` from NuGet.Protocol — rejected because it adds complexity for no benefit on a single read-only endpoint.

### Inject HttpClient via IHttpClientFactory
Register a named `HttpClient` for NuGet search in DI. This follows .NET best practices and avoids socket exhaustion.

### Format download counts as human-readable strings
Return "12.3M" instead of `12345678`. Saves tokens and is more meaningful to agents making popularity comparisons.

### Tool lives in NuGet/ alongside existing tools
`NuGetSearchTool.cs` follows the same pattern as `NuGetDownloadTool.cs` — static method with `[McpServerTool]`, returns `CallToolResult`.

### Search method on INuGetService
Add `SearchAsync` to the existing `INuGetService` interface. The tool delegates to the service, consistent with `nuget_download` → `INuGetService.DownloadAsync`.

## Risks / Trade-offs

- **[Rate limiting]** → NuGet search API is generous (no auth needed, high limits). Not a concern for agent usage patterns.
- **[Stale results]** → Search results reflect NuGet.org's index, which may lag a few minutes behind publishes. Acceptable.
- **[Tangent browsing]** → Agents may explore multiple packages unnecessarily. Mitigated by small result cap and focused tool description.
