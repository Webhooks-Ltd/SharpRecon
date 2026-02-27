## MODIFIED Requirements

### Requirement: Exception safety for all NuGet tools
All NuGet tools (including `nuget_search`) SHALL catch all exceptions and return structured MCP error responses with actionable messages. No exception SHALL propagate to stdout.

#### Scenario: Network failure during download
- **WHEN** a network error occurs during package download
- **THEN** the tool returns a structured error message describing the failure without corrupting the MCP stdio connection

#### Scenario: Malformed version string
- **WHEN** agent passes an invalid version string (e.g. `not.a.version`)
- **THEN** the tool returns a structured error message: "Invalid version format: '{version}'. Use exact version (e.g. '13.0.3'), wildcard (e.g. '13.*'), or omit for latest."

#### Scenario: Network failure during search
- **WHEN** a network error occurs during the NuGet search API call
- **THEN** the tool returns a structured error message describing the failure without corrupting the MCP stdio connection
