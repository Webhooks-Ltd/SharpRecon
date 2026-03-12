## Context

SharpRecon currently requires all assemblies to flow through the NuGet pipeline: `nuget_download` caches a package, and all inspection/decompilation tools take `(packageId, version)` to locate assemblies via `IPackageCache`. The internal chain is: Tool → `IPackageCache` → `AssemblyPathResolver` → `FrameworkAssemblyResolver` / `NuGetDependencyResolver`.

To support local assemblies, we need a parallel entry point (`local_load`) that registers assemblies and produces synthetic identifiers compatible with the existing tool signatures. This avoids changing any existing tool parameters — the LLM learns one new tool and the downstream workflow is identical.

## Goals / Non-Goals

**Goals:**
- Local .NET assemblies (.dll/.exe) and directories of assemblies loadable via a single `local_load` MCP tool
- Existing inspection and decompilation tools work transparently with locally-loaded assemblies via synthetic identifiers
- Dependency resolution for local assemblies via directory probing, framework assemblies, and `.deps.json` when present
- XML doc discovery from `{assemblyName}.xml` in the same directory
- Clear error messages for unsupported formats (single-file bundles, native DLLs)

**Non-Goals:**
- Modifying parameter types/names of any existing MCP tools (descriptions are updated)
- NuGet package restoration for local assemblies (if a dependency isn't local or in the framework, it's unresolved)
- PDB-enhanced decompilation (ICSharpCode.Decompiler uses PDBs automatically if present — no explicit work needed)
- Watching for file changes / hot reload of loaded assemblies
- Loading assemblies from remote URLs or archives

## Decisions

### 1. Synthetic identifier pattern: `local:{name}` / `local`

`local_load` returns `packageId = "local:{AssemblyName}"` (or `"local:{ParentDirName}/{DirName}"` for directories) and `version = "local"`. All existing tools accept these as regular `packageId`/`version` values. Internally, the `local:` prefix routes to local resolution.

For single files, the `packageId` is derived from the assembly name (e.g., `MyApp.dll` → `local:MyApp`). For directories, the `packageId` includes the parent directory to avoid collisions: `C:/projects/MyApp/bin/Debug/net10.0/` → `local:Debug/net10.0`. If a `local_load` is called for a path whose synthetic ID matches an already-registered load, the new registration replaces the old one.

**Why over `assemblyPath` on every tool:** Both expert reviews identified that adding an optional `assemblyPath` to 7+ tools creates mutual-exclusion confusion for LLMs, bloats descriptions, and duplicates validation logic. The synthetic identifier approach requires zero tool signature changes and mirrors the `nuget_download` → inspect flow the LLM already knows.

### 2. `IAssemblySource` abstraction to replace direct `IPackageCache` coupling

The existing code has deep `IPackageCache` coupling throughout `AssemblyInspector` and `AssemblyDecompiler`:
- `AssemblyInspector.SearchTypesAsync` → `_packageCache.GetAssembliesForTfm()`
- `AssemblyInspector.ResolveTypeAsync` → `_packageCache.GetAssembliesForTfm()`
- `AssemblyInspector.SelectBestTfm` → `TfmSelector.SelectBest()` → `_packageCache.GetAvailableTfms()`
- `AssemblyInspector.ResolveXmlDocPath` → `_packageCache.GetPackagePath()`
- `AssemblyDecompiler.ResolveAssemblyForTypeAsync` → `_packageCache.GetAssembliesForTfm()`

Rather than adding `if (isLocal)` branching at each of these call sites, introduce an `IAssemblySource` abstraction:

```csharp
internal interface IAssemblySource
{
    bool IsRegistered(string sourceId, string version);
    IReadOnlyList<string> GetAvailableTfms(string sourceId, string version);
    IReadOnlyList<string> GetAssembliesForTfm(string sourceId, string version, string tfm);
    string? GetAssemblyPath(string sourceId, string version, string tfm, string assemblyName, bool preferRef);
    string? GetXmlDocPath(string sourceId, string version, string tfm, string assemblyName);
}
```

Two implementations:
- `NuGetAssemblySource` — wraps `IPackageCache`, delegates all calls
- `LocalAssemblySource` — wraps `LocalAssemblyRegistry`, ignores `preferRef` (no ref/lib split), returns single inferred TFM for `GetAvailableTfms`

A routing `CompositeAssemblySource` checks the `local:` prefix to dispatch. `AssemblyInspector` and `AssemblyDecompiler` depend on `IAssemblySource` instead of `IPackageCache`.

**Why over keeping `IPackageCache` + branching:** The 5+ call sites in inspectors/decompilers would each need `if (packageId.StartsWith("local:"))` guards. The abstraction centralises routing and makes local assemblies a first-class source.

### 3. `LocalAssemblyRegistry` — in-memory registration store

A `ConcurrentDictionary<string, LocalRegistration>` keyed by the synthetic `packageId` (e.g., `"local:MyApp"`). Each `LocalRegistration` holds:
- `PrimaryPath`: absolute path to the loaded file (or directory)
- `AssemblyPaths`: all managed `.dll`/`.exe` files discovered (native DLLs filtered out during scan)
- `XmlDocPaths`: map of assembly name → XML doc path
- `InferredTfm`: TFM read from `TargetFrameworkAttribute` on the primary assembly
- `DepsJsonPath`: path to `.deps.json` if found alongside

Registered via `local_load`, queried by `LocalAssemblySource` via `IAssemblySource`.

**Why in-memory, not on-disk:** Local assemblies already exist on disk. There's nothing to cache — we just need to remember what was loaded and its metadata. The registry is lightweight and process-scoped.

### 4. `AssemblyPathResolver` split: NuGet vs Local resolution

The existing `AssemblyPathResolver.ResolveAsync` is deeply NuGet-specific — it calls `_packageCache.GetAssemblyPath()`, then walks the NuGet dependency graph via `GlobalCacheAssemblyResolver` and `NuGetDependencyResolver`. This cannot be extended with a local branch.

Instead, add a separate method:

```csharp
public Task<AssemblyResolutionResult> ResolveLocalAsync(
    string primaryAssemblyPath,
    string inferredTfm,
    string? depsJsonPath,
    IReadOnlyList<string> siblingAssemblyPaths,
    CancellationToken ct)
```

This method implements the local resolution tiers (see Decision 5) without touching any NuGet infrastructure. The tool/service layer dispatches to the appropriate method based on `AssemblySource` type.

**Caching:** Local resolution uses a separate `ConcurrentDictionary<string, AssemblyResolutionResult>` keyed by absolute path (not the `{packageId}/{version}/{tfm}` key used for NuGet). Re-load via `local_load` invalidates the cache entry.

### 5. Dependency resolution order for local assemblies

1. **`.deps.json`** — if `{assemblyName}.deps.json` or `{directory}/*.deps.json` exists, parse the `targets` and `libraries` sections using `System.Text.Json` to map assembly names to paths (in the output directory, NuGet global cache, or shared framework store)
2. **Framework assemblies** — infer TFM from `TargetFrameworkAttribute` in assembly metadata, resolve via existing `FrameworkAssemblyResolver`
3. **Sibling directory probing** — scan the assembly's directory for matching `.dll` files by assembly name
4. **Graceful fallback** — anything unresolved is reported in the tool output (same pattern as NuGet resolution)

**`.deps.json` parsing:** Uses `System.Text.Json` directly rather than `Microsoft.Extensions.DependencyModel` (which is a NuGet package, not part of the shared framework). Only the `runtimeTarget`, `targets`, and `libraries` fields are needed — manual parsing avoids a new dependency.

**Why `.deps.json` first:** For build output directories, `.deps.json` is the authoritative source and resolves everything including NuGet package references that were copied to the output directory. Directory probing alone misses assemblies in non-obvious locations.

**Why not NuGet restore for local assemblies:** Out of scope. Local load is about inspecting what's already on disk. If a user needs NuGet dependencies resolved, they should build the project first (which produces the `.deps.json` and copies dependencies to the output directory).

### 6. TFM inference from assembly metadata

For local assemblies, there's no `.nuspec` to read. Instead:
- If `*.runtimeconfig.json` exists alongside the assembly, derive the TFM from its `runtimeOptions.framework.name` + `runtimeOptions.framework.version` (e.g., `Microsoft.NETCore.App` version `10.0.0` → `net10.0`). For the multi-framework format, use `runtimeOptions.frameworks[]`.
- Otherwise, read `TargetFrameworkAttribute` from assembly metadata (e.g., `.NETCoreApp,Version=v10.0` → `net10.0`)
- If neither is available, default to the server's running TFM and warn

For directory loads, TFM priority: (1) `.runtimeconfig.json` in the directory, (2) `TargetFrameworkAttribute` from the `.exe` if one exists, (3) `TargetFrameworkAttribute` from the `.dll` whose name matches the directory name, (4) first `.dll` alphabetically.

**Why not require the user to specify TFM:** The assembly already knows its target. Requiring the user to pass it adds friction and is error-prone.

### 7. Directory loading semantics

When `local_load` receives a directory path:
- Scan for all `.dll` and `.exe` files in the directory (non-recursive)
- Validate each with a CLR header check — register only managed assemblies, silently skip native DLLs
- When both `{name}.exe` and `{name}.dll` exist, skip the `.exe` (it's likely a native apphost stub; the managed assembly is the `.dll`)
- Register managed assemblies under a single synthetic `packageId = "local:{parentDirName}/{dirName}"`
- `assembly_list` returns all discovered managed assemblies under the inferred TFM
- Report skipped native DLLs in the `local_load` output: "{N} assemblies loaded ({M} native DLLs skipped)"

**Why non-recursive:** Build output directories are flat. Recursive scanning would pick up `runtimes/` subdirectories with platform-specific variants, causing duplicate type confusion.

### 8. Validation and error handling

| Input | Validation | Error |
|---|---|---|
| Path doesn't exist | `File.Exists` / `Directory.Exists` | "Path not found: {path}" |
| Native DLL (no CLR header) | `PEHeaders.CorHeader is null` | "Not a managed .NET assembly: {filename}" |
| Single-file bundle | Search last ~4KB for bundle signature `0xd0e52d67b9e2400a8e4186f9b54d8352` | "Single-file published assemblies are not supported. Extract the bundle first." |
| Mixed-mode assembly (C++/CLI) | `PEHeaders.CorHeader is not null && !CorHeader.Flags.HasFlag(CorFlags.ILOnly)` | Warning appended to decompilation output (not blocked) |
| Empty directory | No managed `.dll`/`.exe` found | "No .NET assemblies found in directory: {path}" |
| Relative path | Resolve via `Path.GetFullPath` | (no error — normalised automatically) |
| `.exe` apphost stub | When both `{name}.exe` and `{name}.dll` exist in a directory, skip the `.exe` | (no error — silently skipped) |

### 9. Tool description and validation updates

Existing tools need two non-signature changes to work with local sources:
- **Parameter descriptions**: Update `packageId` descriptions from `"NuGet package ID"` to `"Package or local load identifier (from nuget_download or local_load)"`. Update `version` descriptions similarly.
- **Version validation**: `ToolHelper.ValidateExactVersion(version)` currently rejects wildcards. Tools SHALL skip this validation when `packageId` starts with `local:` (version is always the literal `"local"`).
- **Tool descriptions**: `assembly_list` description updated from "Lists assemblies in a cached NuGet package" to cover both sources.

### 10. `local_load` output format

The `local_load` tool returns structured text that guides the LLM to the next step:

```
Source: local:MyApp
Version: local
Path: C:/projects/MyApp/bin/Debug/net10.0/MyApp.dll
Inferred TFM: net10.0
Assemblies loaded: 1

Next: use assembly_list, type_list, type_search, or decompile_type with packageId="local:MyApp" version="local"
```

For directory loads with native DLL filtering:
```
Source: local:Debug/net10.0
Version: local
Path: C:/projects/MyApp/bin/Debug/net10.0/
Inferred TFM: net10.0
Assemblies loaded: 12 (3 native DLLs skipped)

Next: use assembly_list, type_list, type_search, or decompile_type with packageId="local:Debug/net10.0" version="local"
```

The "Next:" line is critical for LLM UX — it tells the agent exactly how to chain tools, mirroring how `nuget_download` guides to the next step.

## Risks / Trade-offs

**[Risk] `.deps.json` parsing adds complexity** → `.deps.json` support can be implemented as an enhancement after the initial directory-probing path works. The resolution order is designed so each tier is independent — skip tier 1 for v1 if needed.

**[Risk] Mixed-mode assemblies (C++/CLI) produce confusing output** → Native methods decompile to empty bodies. Mitigation: detect via `!CorFlags.ILOnly` and include a warning: "This is a mixed-mode assembly. Native method bodies cannot be decompiled."

**[Risk] Assembly name collisions with NuGet packages** → A local assembly named `Newtonsoft.Json.dll` would get `packageId = "local:Newtonsoft.Json"`, which won't collide with the NuGet `packageId = "Newtonsoft.Json"`. The `local:` prefix ensures namespace separation.

**[Risk] Local-to-local directory name collisions** → Using `{parentDir}/{dirName}` reduces but doesn't eliminate collisions. If two loads collide, the new registration replaces the old one. Documented as known limitation — users must be aware that loading a second directory with the same parent/name replaces the first.

**[Risk] Large directories with many assemblies** → A published self-contained app may have 200+ framework DLLs. Mitigation: `assembly_list` output is already paginated/capped, and the user typically targets specific types, not all assemblies.

**[Trade-off] No hot reload** → If the user rebuilds their project, they must call `local_load` again. This matches the NuGet model (re-download for new versions) and keeps the registry simple.

**[Trade-off] `IAssemblySource` abstraction introduces a new interface** → Adds an interface and three implementations (NuGet, Local, Composite). This is justified by the 5+ call sites in inspectors/decompilers that would otherwise each need branching logic. The abstraction centralises routing and makes local assemblies a first-class source.
