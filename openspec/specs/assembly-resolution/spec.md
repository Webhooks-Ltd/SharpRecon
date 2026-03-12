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
The assembly resolver SHALL return a structured result containing all resolved paths and a list of unresolved dependencies. For NuGet assemblies, paths come from the package cache, global cache, and NuGet protocol downloads. For local assemblies, paths come from directory probing, `.deps.json` resolution, and framework assemblies.

#### Scenario: Fully resolved
- **WHEN** all framework and package dependencies are found
- **THEN** the result contains the primary assembly path, framework assembly paths, dependency assembly paths, and an empty unresolved list

#### Scenario: Partially resolved
- **WHEN** some dependencies cannot be found or downloaded
- **THEN** the result contains all successfully resolved paths and the unresolved list names the missing assemblies

#### Scenario: Unresolved dependencies reported to agent
- **WHEN** an MCP tool uses the resolver and there are unresolved dependencies
- **THEN** the tool response includes a note: "Note: {count} dependencies could not be resolved: {list}. Results referencing these assemblies may be incomplete."

#### Scenario: Local assembly with mixed resolution sources
- **WHEN** a locally-loaded assembly's dependencies are resolved from both sibling directory probing and framework assemblies
- **THEN** the result contains paths from all sources, deduplicated by full path

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

### Requirement: Local assembly dependency resolution
The assembly resolver SHALL resolve dependencies for locally-loaded assemblies using a tiered strategy that does not depend on NuGet package metadata. Resolution order: (1) `.deps.json` if present, (2) framework assemblies via inferred TFM, (3) sibling directory probing, (4) unresolved fallback. This is implemented as a separate `ResolveLocalAsync` method on `AssemblyPathResolver`, not as a branch within the existing NuGet resolution method.

#### Scenario: Resolution via .deps.json
- **WHEN** a `.deps.json` file exists alongside the loaded assembly and lists runtime dependencies
- **THEN** the resolver parses the `runtimeTarget`, `targets`, and `libraries` fields using `System.Text.Json` to locate assembly paths (in the output directory, NuGet global cache, or shared framework store)

#### Scenario: Resolution via sibling directory probing
- **WHEN** no `.deps.json` is present and the loaded assembly references `Newtonsoft.Json`
- **THEN** the resolver probes the assembly's directory for `Newtonsoft.Json.dll` and includes it if found

#### Scenario: Framework assembly resolution for local assemblies
- **WHEN** a locally-loaded assembly targets `net10.0` (inferred from `TargetFrameworkAttribute` or `.runtimeconfig.json`)
- **THEN** the resolver provides framework reference assemblies from the targeting pack, using the existing `FrameworkAssemblyResolver`

#### Scenario: Graceful fallback for unresolved dependencies
- **WHEN** a dependency cannot be found via any resolution tier
- **THEN** the resolver reports it as unresolved in the result, and tools include the unresolved dependency list in their output

#### Scenario: No .deps.json, no sibling DLL
- **WHEN** an assembly references `SomeLib` and neither `.deps.json` exists nor `SomeLib.dll` is in the same directory
- **THEN** the resolver reports `SomeLib` as unresolved and continues without error

### Requirement: Local resolution does not use IPackageCache or NuGet infrastructure
Resolution for locally-loaded assemblies (`local:` prefix) SHALL use `LocalAssemblyRegistry` and directory probing via the `IAssemblySource` abstraction. The NuGet dependency resolution pipeline (nuspec reading, `GlobalCacheAssemblyResolver`, `NuGetDependencyResolver`) SHALL NOT be invoked for local assemblies.

#### Scenario: Local assembly does not query NuGet
- **WHEN** a tool is called with `packageId = "local:MyApp"` and the assembly has unresolved dependencies
- **THEN** the resolver does NOT attempt to download packages from NuGet — it reports them as unresolved

### Requirement: Assembly resolution caching for local assemblies
Resolved dependency information for local assemblies SHALL be cached in a separate cache dictionary keyed by the absolute path of the loaded file or directory. This is separate from the NuGet resolution cache (which keys by `{packageId}/{version}/{tfm}`). Re-loading via `local_load` SHALL invalidate the cache entry for that path.

#### Scenario: Repeated tool calls for same local assembly
- **WHEN** an agent calls multiple tools for the same locally-loaded assembly
- **THEN** dependency resolution runs once and subsequent calls use the cached result

#### Scenario: Re-load invalidates cache
- **WHEN** an agent calls `local_load` for a path that was previously loaded
- **THEN** the resolution cache for that path is cleared, and the next tool call triggers fresh resolution

### Requirement: IAssemblySource abstraction
An `IAssemblySource` interface SHALL abstract assembly location queries, replacing direct `IPackageCache` usage in `AssemblyInspector` and `AssemblyDecompiler`. Two implementations: `NuGetAssemblySource` (wraps `IPackageCache`) and `LocalAssemblySource` (wraps `LocalAssemblyRegistry`). A `CompositeAssemblySource` routes based on the `local:` prefix.

#### Scenario: NuGet source routing
- **WHEN** a tool is called with `packageId = "Newtonsoft.Json"` (no `local:` prefix)
- **THEN** `CompositeAssemblySource` delegates to `NuGetAssemblySource`, which calls through to `IPackageCache`

#### Scenario: Local source routing
- **WHEN** a tool is called with `packageId = "local:MyApp"`
- **THEN** `CompositeAssemblySource` delegates to `LocalAssemblySource`, which queries `LocalAssemblyRegistry`

#### Scenario: Local source ignores preferRef
- **WHEN** `LocalAssemblySource.GetAssemblyPath` is called with `preferRef = true`
- **THEN** the parameter is ignored (local assemblies have no ref/lib split) and the file is returned directly

#### Scenario: Local source returns single TFM
- **WHEN** `LocalAssemblySource.GetAvailableTfms` is called for a local load
- **THEN** it returns a single-element list containing the inferred TFM

