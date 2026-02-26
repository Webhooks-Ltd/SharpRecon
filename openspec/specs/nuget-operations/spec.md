# nuget-operations Specification

## Purpose
TBD - created by archiving change initial-mcp-server. Update Purpose after archive.
## Requirements
### Requirement: Download NuGet package
The `nuget_download` tool SHALL download a NuGet package to the standard global packages cache (`~/.nuget/packages` or `NUGET_PACKAGES` env var) using `NuGet.Packaging.PackageExtractor` or equivalent to ensure cache entries are identical to `dotnet restore` output (including `.nupkg.metadata` and `.nupkg.sha512` files). The tool SHALL accept an optional version parameter supporting exact versions, wildcard patterns, or omission for latest stable. The tool SHALL only download from nuget.org (hardcoded source).

#### Scenario: Download with exact version
- **WHEN** agent calls `nuget_download` with packageId `Newtonsoft.Json` and version `13.0.3`
- **THEN** the package is downloaded to the global cache and the response contains the package ID, resolved version `13.0.3`, cache path, and list of available TFMs

#### Scenario: Download with wildcard version
- **WHEN** agent calls `nuget_download` with packageId `Newtonsoft.Json` and version `13.*`
- **THEN** the system queries NuGet for the highest stable version matching `>= 13.0.0, < 14.0.0`, downloads it, and returns the resolved version

#### Scenario: Download with minor wildcard
- **WHEN** agent calls `nuget_download` with packageId `Newtonsoft.Json` and version `13.0.*`
- **THEN** the system queries NuGet for the highest stable version matching `>= 13.0.0, < 13.1.0`, downloads it, and returns the resolved version

#### Scenario: Download latest stable
- **WHEN** agent calls `nuget_download` with packageId `Newtonsoft.Json` and no version
- **THEN** the system queries NuGet for the latest stable version, downloads it, and returns the resolved version

#### Scenario: Wildcard resolution is always fresh
- **WHEN** agent calls `nuget_download` with version `2.*` multiple times
- **THEN** each call re-queries NuGet for the highest matching version (no caching of wildcard resolution)

#### Scenario: Package not found
- **WHEN** agent calls `nuget_download` with a packageId that does not exist on nuget.org
- **THEN** the tool returns a structured error message: "Package '{packageId}' not found on nuget.org"

#### Scenario: Version not found
- **WHEN** agent calls `nuget_download` with a version or wildcard that matches no published versions
- **THEN** the tool returns a structured error message: "No version matching '{version}' found for package '{packageId}'"

#### Scenario: Package already cached
- **WHEN** agent calls `nuget_download` for a package and exact version already present in the global cache
- **THEN** the tool returns success immediately without re-downloading

### Requirement: List assemblies in a package
The `assembly_list` tool SHALL list all DLL files in a cached NuGet package, optionally filtered by TFM. The tool SHALL also return the full list of available TFMs. The tool SHALL accept exact versions only (not wildcards).

#### Scenario: List all assemblies
- **WHEN** agent calls `assembly_list` with packageId and version but no TFM filter
- **THEN** the tool returns all DLL names (without `.dll` extension) grouped by their TFM folder, plus the full list of available TFMs

#### Scenario: List assemblies for specific TFM
- **WHEN** agent calls `assembly_list` with a `tfm` parameter of `net10.0`
- **THEN** the tool returns only DLL names from the `lib/net10.0/` (and `ref/net10.0/` if present) directory

#### Scenario: TFM not available in package
- **WHEN** agent calls `assembly_list` with a TFM that does not exist in the package
- **THEN** the tool returns a structured error message listing the available TFMs: "TFM 'net7.0' not available. Available TFMs: net10.0, net8.0, netstandard2.0"

#### Scenario: Package not in cache
- **WHEN** agent calls `assembly_list` for a package that is not in the global cache
- **THEN** the tool returns a structured error message: "Package '{packageId}' version '{version}' not found in cache. Call nuget_download first."

#### Scenario: Package with runtimes directory
- **WHEN** a package has empty `lib/` folders (only `_._` markers) but has `runtimes/{rid}/lib/{tfm}/` directories
- **THEN** the tool includes assemblies from the current platform's runtime-specific directory

### Requirement: Exception safety for all NuGet tools
All NuGet tools SHALL catch all exceptions and return structured MCP error responses with actionable messages. No exception SHALL propagate to stdout.

#### Scenario: Network failure during download
- **WHEN** a network error occurs during package download
- **THEN** the tool returns a structured error message describing the failure without corrupting the MCP stdio connection

#### Scenario: Malformed version string
- **WHEN** agent passes an invalid version string (e.g. `not.a.version`)
- **THEN** the tool returns a structured error message: "Invalid version format: '{version}'. Use exact version (e.g. '13.0.3'), wildcard (e.g. '13.*'), or omit for latest."

