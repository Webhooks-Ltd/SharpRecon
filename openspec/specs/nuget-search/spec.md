# nuget-search Specification

## Purpose
TBD - created by archiving change add-nuget-search. Update Purpose after archive.
## Requirements
### Requirement: Search NuGet packages by query
The `nuget_search` tool SHALL search NuGet.org for packages matching a query string and return a list of results containing package ID, latest stable version, truncated description, total download count, and verified owner status.

#### Scenario: Basic search
- **WHEN** agent calls `nuget_search` with query `json serializer`
- **THEN** the tool returns up to 10 results, each with packageId, version (latest stable), description (truncated to ~200 chars), totalDownloads (human-formatted e.g. "12.3M"), and verified (boolean)

#### Scenario: Search with custom result count
- **WHEN** agent calls `nuget_search` with query `serilog` and take `5`
- **THEN** the tool returns up to 5 results

#### Scenario: Maximum result cap
- **WHEN** agent calls `nuget_search` with take greater than 20
- **THEN** the tool caps results at 20

#### Scenario: No results found
- **WHEN** agent calls `nuget_search` with a query that matches no packages
- **THEN** the tool returns an empty result with message "No packages found matching '{query}'"

#### Scenario: Empty query
- **WHEN** agent calls `nuget_search` with an empty or whitespace-only query
- **THEN** the tool returns a structured error message: "Search query must not be empty"

### Requirement: Search result formatting
The `nuget_search` tool SHALL format results as a markdown summary optimized for LLM consumption with minimal token footprint.

#### Scenario: Formatted output
- **WHEN** search returns results
- **THEN** each result is formatted as a numbered list with package ID, version, verified badge, download count, and description on a single block per result

### Requirement: Exception safety for search
The `nuget_search` tool SHALL catch all exceptions and return structured MCP error responses. No exception SHALL propagate to stdout.

#### Scenario: Network failure during search
- **WHEN** a network error occurs during the search API call
- **THEN** the tool returns a structured error message describing the failure

