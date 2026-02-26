# assembly-resolution Specification

## Purpose
TBD - created by archiving change initial-mcp-server. Update Purpose after archive.
## Requirements
### Requirement: Framework assembly resolution
The assembly resolver SHALL discover installed .NET targeting packs on disk and provide framework reference assemblies for any requested TFM. Discovery SHALL be cross-platform: resolve dotnet root via (1) `DOTNET_ROOT` environment variable, (2) walking up from `RuntimeEnvironment.GetRuntimeDirectory()`, (3) platform-specific defaults. Targeting packs are at `{dotnetRoot}/packs/Microsoft.NETCore.App.Ref/{version}/ref/{tfm}/`.

#### Scenario: Resolve framework assemblies for a net10.0 assembly
- **WHEN** an assembly targets `net10.0` and the machine has the .NET 10 targeting pack installed
- **THEN** the resolver provides paths to all framework reference assemblies from the targeting pack's `ref/net10.0/` directory

#### Scenario: Fall back to running runtime
- **WHEN** the requested TFM's targeting pack is not installed
- **THEN** the resolver falls back to the running runtime directory's assemblies

#### Scenario: Runtime fallback limitation
- **WHEN** runtime directory assemblies are used as fallback (no targeting pack)
- **THEN** type forwarding chains (`[TypeForwardedTo]`) may not resolve correctly. This is a known limitation — targeting packs are the correct path.

#### Scenario: netstandard targeting
- **WHEN** an assembly targets `netstandard2.0`
- **THEN** the resolver provides runtime assemblies as facades (netstandard.dll forwards to runtime types)

### Requirement: NuGet dependency resolution via .nuspec
The assembly resolver SHALL read the `.nuspec` from the inspected package to determine declared dependency package IDs and version ranges. It SHALL look up these dependencies by package ID in the NuGet global cache. It SHALL NOT scan the global cache by assembly filename — the mapping from assembly name to package ID is not deterministic.

#### Scenario: Dependency found in global cache by package ID
- **WHEN** the `.nuspec` declares a dependency on package `Microsoft.Extensions.Logging` version `>= 9.0.0` and that package exists in `~/.nuget/packages/microsoft.extensions.logging/9.0.0/`
- **THEN** the resolver returns the path to the cached assembly for the best-matching TFM without any network I/O

#### Scenario: TFM compatibility matching
- **WHEN** the global cache contains a dependency package with multiple TFM folders
- **THEN** the resolver uses `NuGet.Frameworks.FrameworkReducer.GetNearest()` to select the best TFM match

#### Scenario: Dependency not in global cache
- **WHEN** a declared dependency package is not found in the global cache
- **THEN** the resolver proceeds to NuGet Protocol download (tier 3)

### Requirement: NuGet Protocol dependency download
The assembly resolver SHALL resolve the transitive dependency graph via NuGet Protocol and download missing packages to the global cache using `NuGet.Packaging.PackageExtractor` for safe extraction.

#### Scenario: Resolve and download transitive dependencies
- **WHEN** a package has transitive dependencies not present in the global cache
- **THEN** the resolver queries NuGet for the dependency graph, downloads missing packages, and provides paths to all resolved assemblies

#### Scenario: Depth limit on dependency graph
- **WHEN** the transitive dependency graph exceeds 6 levels deep
- **THEN** the resolver stops traversing and reports dependencies beyond the limit as unresolved

#### Scenario: Network failure during dependency download
- **WHEN** NuGet API calls fail due to network issues
- **THEN** the resolver reports the failed dependencies as unresolved and continues with whatever assemblies it could resolve (graceful degradation)

#### Scenario: Dependency version range resolution
- **WHEN** a package dependency specifies a version range (e.g. `>= 6.0.0, < 7.0.0`)
- **THEN** the resolver picks the lowest version satisfying the range (NuGet default behavior) from available versions

### Requirement: Composite resolution result
The assembly resolver SHALL return a structured result containing all resolved paths and a list of unresolved dependencies.

#### Scenario: Fully resolved
- **WHEN** all framework and package dependencies are found
- **THEN** the result contains the primary assembly path, framework assembly paths, dependency assembly paths, and an empty unresolved list

#### Scenario: Partially resolved
- **WHEN** some dependencies cannot be found or downloaded
- **THEN** the result contains all successfully resolved paths and the unresolved list names the missing packages with their versions

#### Scenario: Unresolved dependencies reported to agent
- **WHEN** an MCP tool uses the resolver and there are unresolved dependencies
- **THEN** the tool response includes a note: "Note: {count} dependencies could not be resolved: {list}. Results referencing these assemblies may be incomplete."

### Requirement: Assembly resolution caching
Resolved dependency graphs for exact (packageId, version, TFM) tuples SHALL be cached for the lifetime of the server process to avoid redundant resolution.

#### Scenario: Repeated inspection of the same package
- **WHEN** an agent calls multiple tools for the same package, version, and TFM
- **THEN** the dependency graph is resolved once and subsequent calls use the cached result

#### Scenario: Different TFM for same package
- **WHEN** an agent inspects the same package with different TFMs
- **THEN** each TFM gets its own cached resolution since dependency graphs can differ per TFM

### Requirement: Packages with runtimes directory
The assembly resolver SHALL handle packages that use `runtimes/{rid}/lib/{tfm}/` directories instead of or in addition to `lib/{tfm}/`.

#### Scenario: Empty lib with runtime-specific assemblies
- **WHEN** a package has `lib/{tfm}/_._` marker files and `runtimes/{rid}/lib/{tfm}/` directories
- **THEN** the resolver looks in the `runtimes/` directory for the current platform's RID (via `RuntimeInformation.RuntimeIdentifier`)

#### Scenario: Both lib and runtimes present
- **WHEN** a package has assemblies in both `lib/{tfm}/` and `runtimes/{rid}/lib/{tfm}/`
- **THEN** the resolver prefers `lib/` for inspection (cross-platform API surface) and notes the availability of runtime-specific assemblies

### Requirement: Exception safety for assembly resolution
Assembly resolution failures SHALL never crash the MCP server or corrupt the stdio connection.

#### Scenario: Corrupted package in cache
- **WHEN** a cached package has corrupted or missing DLL files
- **THEN** the resolver reports the assembly as unresolved and continues without throwing

#### Scenario: NuGet API timeout
- **WHEN** the NuGet API does not respond within 10 seconds for a single package
- **THEN** the resolver skips that dependency, reports it as unresolved, and continues resolving others

#### Scenario: Malformed .nuspec
- **WHEN** a package's `.nuspec` file is missing or cannot be parsed
- **THEN** the resolver logs a warning, skips dependency resolution for that package, and reports all dependencies as unresolved

