## ADDED Requirements

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

## MODIFIED Requirements

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
