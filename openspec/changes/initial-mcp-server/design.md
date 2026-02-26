## Context

SharpRecon is a greenfield .NET 10.0 stdio MCP server. No existing code. The server will be used by AI coding agents (Claude Code, Cursor, VS Code Copilot) to explore .NET APIs by downloading NuGet packages and inspecting their assemblies — types, signatures, XML docs, and decompiled source.

The MCP protocol uses JSON-RPC over stdio. Any unhandled exception or stray console output will corrupt the protocol stream and kill the connection. This makes exception handling a first-class architectural concern.

## Goals / Non-Goals

**Goals:**
- Provide AI agents with structured access to any public .NET API via NuGet
- Support full type introspection: signatures, generics, constraints, XML docs, decompiled source
- Handle assembly dependencies transparently so results are complete
- Never crash the MCP stdio connection — all exceptions must be caught and returned as structured error results
- Support flexible version matching including wildcards (`2.*`, `2.1.*`)

**Non-Goals:**
- Private/internal type inspection — public API surface only. Agents exploring packages need the consumer-facing API; internals add noise and double the output size. Can revisit with an opt-in flag if there's demand.
- Whole-assembly decompilation (too much output for agent context windows)
- Multi-source NuGet feeds (nuget.org only for v1, enforced in implementation — no configurable feed URL)
- Hosting as an HTTP MCP server (stdio only for v1)
- Project/solution-level analysis (this is package-level inspection)

## Tool Surface

The MCP tools are the product. Every other decision serves these.

All inspection and decompilation tools accept **exact versions only**. The agent MUST use the resolved version returned by `nuget_download`. Wildcard and latest resolution is only supported by `nuget_download`. This avoids ambiguity about which cached version to inspect and eliminates redundant NuGet API calls on every inspection tool invocation.

All `assemblyName` parameters accept the assembly name **without** the `.dll` extension (e.g. `Newtonsoft.Json`, not `Newtonsoft.Json.dll`). Matching is case-insensitive.

### `nuget_download`
Downloads a NuGet package to the local cache. Returns resolved version, cache path, and available TFMs. Call this first — all other tools require the exact version from this response.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `packageId` | string | yes | NuGet package ID, e.g. `"Newtonsoft.Json"` |
| `version` | string | no | Exact version, wildcard pattern (`"2.*"`, `"2.1.*"`), or omit for latest stable |

Returns: package ID, resolved version, cache path, list of available TFMs.

### `assembly_list`
Lists assemblies in a cached NuGet package, grouped by TFM. Use to discover assembly names before calling type_list or type_search.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `packageId` | string | yes | NuGet package ID |
| `version` | string | yes | Exact package version (from nuget_download) |
| `tfm` | string | no | Target framework moniker to filter by, e.g. `"net8.0"`. Omit to list all TFMs. |

Returns: assembly names grouped by TFM, plus the full list of available TFMs.

### `type_list`
Lists all public types in an assembly, grouped by namespace. Returns type full names and kinds (class, struct, enum, interface, delegate). For name-based search, use type_search instead.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `packageId` | string | yes | NuGet package ID |
| `version` | string | yes | Exact package version (from nuget_download) |
| `assemblyName` | string | yes | Assembly name without .dll extension, e.g. `"Newtonsoft.Json"` |
| `tfm` | string | no | TFM filter, e.g. `"net8.0"`. Omit to auto-select highest: net* > netstandard* > netcoreapp*. |
| `namespace` | string | no | Filter to types in this namespace only |

Returns: type full names grouped by namespace, with kind. Record detection is best-effort heuristic (see Decision 8).

### `type_search`
Searches for public types by name across all assemblies in a package. Case-insensitive substring match. Returns matching type full names, kinds, and containing assemblies. For a full listing of one assembly, use type_list instead.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `packageId` | string | yes | NuGet package ID |
| `version` | string | yes | Exact package version (from nuget_download) |
| `query` | string | yes | Search string (case-insensitive substring match) |
| `tfm` | string | no | TFM filter. Omit to auto-select highest available. |
| `assemblyName` | string | no | Scope search to a single assembly (without .dll extension) |
| `maxResults` | int | no | Maximum results (default 100) |

Returns: matching type full names with kind and assembly.

### `type_detail`
Returns the full C# type declaration: XML doc summary, base types, and all public member signatures grouped by kind. Use after type_list/type_search to inspect a type. For implementation source, use decompile_type.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `packageId` | string | yes | NuGet package ID |
| `version` | string | yes | Exact package version (from nuget_download) |
| `typeName` | string | yes | Fully qualified type name, e.g. `"Newtonsoft.Json.JsonConvert"` |
| `tfm` | string | no | TFM filter. Omit to auto-select highest available. |
| `assemblyName` | string | no | Assembly name hint (without .dll). Speeds lookup when package has many assemblies. |

Returns: full C# type declaration (generics, constraints, base type, interfaces), XML doc summary, all public members grouped by kind with signatures.

### `member_detail`
Returns all overload signatures and XML docs for a specific member of a type. Use after type_detail to drill into one member. For implementation source, use decompile_member.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `packageId` | string | yes | NuGet package ID |
| `version` | string | yes | Exact package version (from nuget_download) |
| `typeName` | string | yes | Fully qualified type name, e.g. `"Newtonsoft.Json.JsonConvert"` |
| `memberName` | string | yes | Member name, e.g. `"SerializeObject"`. Use `".ctor"` for constructors. |
| `parameterTypes` | string[] | no | Fully qualified CLR parameter types for overload filtering, e.g. `["System.Object", "System.String"]`. Use CLR names, not C# aliases. |
| `tfm` | string | no | TFM filter. Omit to auto-select highest available. |
| `assemblyName` | string | no | Assembly name hint (without .dll). Speeds lookup when package has many assemblies. |

Returns: all overloads (or filtered overload) with full signatures (return type, parameters, generics, constraints, attributes, default values) and XML docs per overload (summary, params, returns, exceptions, remarks).

### `decompile_type`
Decompiles a type to full C# source code. Use when you need implementation details, not just signatures. For signatures and XML docs only, use type_detail instead.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `packageId` | string | yes | NuGet package ID |
| `version` | string | yes | Exact package version (from nuget_download) |
| `typeName` | string | yes | Fully qualified type name, e.g. `"Newtonsoft.Json.JsonConvert"` |
| `tfm` | string | no | TFM filter. Omit to auto-select highest available. |
| `maxLength` | int | no | Max output characters (default 50000). Truncates if exceeded. |

Returns: C# source code of the full type.

### `decompile_member`
Decompiles a single member to C# source code. Use parameterTypes to disambiguate overloads. For all overload signatures without source, use member_detail instead.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `packageId` | string | yes | NuGet package ID |
| `version` | string | yes | Exact package version (from nuget_download) |
| `typeName` | string | yes | Fully qualified type name, e.g. `"Newtonsoft.Json.JsonConvert"` |
| `memberName` | string | yes | Member name, e.g. `"SerializeObject"`. Use `".ctor"` for constructors. |
| `parameterTypes` | string[] | no | Fully qualified CLR parameter types for overload disambiguation, e.g. `["System.Object", "System.String"]`. Use CLR names, not C# aliases. |
| `tfm` | string | no | TFM filter. Omit to auto-select highest available. |

Returns: C# source code of the member (or specific overload).

## Decisions

### 1. Single project, organized by capability

**Choice**: One `SharpRecon.csproj` with folders: `NuGet/`, `Inspection/`, `Decompilation/`, `Infrastructure/`.

**Why**: Focused CLI tool with no reuse scenario. Multi-project adds build complexity and ceremony with zero payoff. If a library need emerges, extract then.

**Alternative**: Separate `SharpRecon.Core` library + `SharpRecon.Server` host. Rejected — no consumer for the library today.

### 2. Interfaces at I/O boundaries only

**Choice**: `INuGetService`, `IAssemblyInspector`, `IPackageCache` get interfaces. `TypeRenderer` (static/pure), `XmlDocParser` (singleton, stateful with cache), `MetadataLoadContextFactory` (hidden behind IAssemblyInspector), `AssemblyDecompiler` (tested end-to-end against real assemblies), `AssemblyPathResolver` (tested end-to-end) do not.

**Why**: The test: "does this perform I/O that consumers need insulated from, AND would a fake provide meaningful test coverage?" `INuGetService` — yes, tools need deterministic results without network calls. `IAssemblyInspector` — yes, tools need canned results for edge cases. `IPackageCache` — yes, tests need temp directories. The decompiler and assembly path resolver are best tested end-to-end against real packages; faking them gives false confidence about output fidelity.

### 3. Three-tier assembly dependency resolution

**Choice**: Resolve assembly references via (1) framework targeting packs on disk, (2) NuGet global cache lookup by declared dependency package IDs from `.nuspec`, (3) NuGet Protocol download on demand. Orchestrated by a concrete `AssemblyPathResolver` returning an `AssemblyResolutionResult` with resolved paths + list of unresolved dependencies.

**Tier 2 strategy**: Read the `.nuspec` from the inspected package to get its declared dependencies (package IDs + version ranges), then look those up in the global cache by package ID. Do NOT scan the global cache blindly by assembly filename — the mapping from assembly name to package ID is not deterministic. Use `NuGet.Frameworks.FrameworkReducer.GetNearest()` for TFM compatibility matching within a cached package's `lib/` directory.

**Why**: Without dependency resolution, `MetadataLoadContext` throws on `type.BaseType` and the decompiler produces degraded output. Three tiers mean the common case is offline and fast, while fresh packages still work via NuGet API.

**Depth limit**: 6 levels of transitive dependencies. Configurable. Common packages in the `Microsoft.Extensions.*` ecosystem routinely have dependency chains 4-5 levels deep. A limit of 3 would produce excessive "unresolved" noise on mainstream packages. 6 covers all common cases while still preventing runaway resolution on meta-packages.

**Alternative**: "Best-effort global cache scan by assembly filename." Rejected — assembly names don't map deterministically to package IDs, fails on packages where `assemblyName != packageId`.

### 4. Expensive objects created per-request, not cached

**Choice**: Both `MetadataLoadContext` and `CSharpDecompiler` are created per tool call and disposed immediately after.

**Why**: Both pin file handles and memory. `CSharpDecompiler` can consume 100-200MB for large assemblies and is not thread-safe. `MetadataLoadContext` pins assembly files — on Windows, this means file locking conflicts if two contexts try to open the same assembly simultaneously. For a stdio server with serialized heavy operations (see Decision 11), creation cost is negligible. Caching would accumulate unbounded memory across different package/TFM combinations. If profiling shows this matters, add bounded LRU cache with explicit disposal later.

**Known constraint**: On Windows, `MetadataLoadContext` file locking means relaxing the concurrency semaphore would require careful lifecycle management to avoid `IOException`. This is acceptable under the current serialized-heavy-operations design.

### 5. Exception handling — structured error responses, never stdout

**Choice**: Every MCP tool method catches all exceptions and returns them as structured MCP error responses via the `ModelContextProtocol` SDK's tool result type. No exception may propagate to stdout. Logging goes to stderr exclusively.

**Why**: The MCP protocol runs over stdio JSON-RPC. Unhandled exceptions writing to stdout corrupt the protocol stream and kill the connection. This is a correctness requirement.

**Approach**: The `ModelContextProtocol` 1.0.0 SDK's `[McpServerTool]` methods return results that the framework serializes as JSON-RPC responses. Tool methods should catch all exceptions and return error results with `isError: true` and user-friendly, actionable error messages (e.g. "Package 'Foo' not found in cache. Call nuget_download first." or "TFM 'net7.0' not available. Available TFMs: net10.0, net8.0, netstandard2.0"). The exact SDK types (`McpToolResult`, content types, etc.) will be confirmed against the 1.0.0 API surface during initial project scaffolding before any tool implementation begins.

### 6. XML docs — stateful singleton with cache

**Choice**: `XmlDocParser` is a singleton service registered in DI. It owns an internal `ConcurrentDictionary<string, XmlDocCollection?>` cache keyed by `"{packageId}/{version}/{tfm}/{assemblyName}"`. It accepts file paths (resolved by the caller via `IPackageCache`), parses the XML file on first access, and caches the result.

**Why**: XML doc files are immutable per (package, version, tfm, assembly). Parsing them repeatedly is wasteful. The caller (`AssemblyInspector`) resolves the file path and passes it in. `XmlDocParser` is stateful (it caches) but its I/O is simple file reads of known immutable files — not worth an interface. For unit testing `XmlDocParser` in isolation, pass paths to test XML fixture files.

### 7. Signature rendering as pure static functions

**Choice**: `TypeRenderer` is a static class with pure methods — Type metadata in, C# declaration strings out.

**Why**: No state, no I/O, no side effects. Directly testable by asserting on string output. Most likely component to have subtle bugs (generics, nullability, `ref`/`in`/`out`, `params`, default values) so maximizing testability matters.

**Nullability annotations**: Rendering `?` nullability requires reading `NullableAttribute` and `NullableContextAttribute` from assembly metadata. The encoding uses a compact byte array format where `0 = oblivious`, `1 = not annotated`, `2 = annotated`, with the encoding varying for generic type trees. This is the single most complex part of `TypeRenderer` and should be implemented carefully with extensive test coverage against known annotated assemblies. Reference: Roslyn nullable metadata specification.

**Record detection**: Records are not marked with a dedicated IL metadata flag. Detection is heuristic — look for compiler-synthesized `PrintMembers`, `EqualityContract`, and clone methods. This will be best-effort and documented as such. Records that can't be detected will render as `class` or `struct`.

**`async` keyword**: The `async` modifier is NOT present in assembly metadata — it is a C# compiler implementation detail. Methods returning `Task<T>` or `ValueTask<T>` are not necessarily `async`. `TypeRenderer` will NOT attempt to render `async`. If the agent needs to know whether a method uses `async`, they can use `decompile_member`.

### 8. TFM selection strategy

**Choice**: When TFM is not specified by the caller, prefer the highest `net*` TFM available, then fall back to highest `netstandard*`, then highest `netcoreapp*`. Within the same family, prefer higher versions.

**Why**: Agents typically want the most modern API surface. Higher TFMs expose more APIs and use newer language features. `netstandard` is the fallback since it's the broadest compatibility target.

**Override**: The agent can always pass an explicit `tfm` parameter to select a specific framework.

### 9. Package version resolution and wildcard matching

**Choice**: Support three version formats on `nuget_download` only:
- **Exact** (`13.0.3`) — use that version directly
- **Wildcard** (`2.*`, `2.1.*`, `13.*`) — resolve to the highest stable version matching the pattern
- **Omitted** — resolve to the latest stable version

All other tools (inspection, decompilation) accept **exact versions only**. The agent uses the resolved version from `nuget_download`.

Wildcard matching uses NuGet version range semantics. `2.*` matches `>= 2.0.0, < 3.0.0`. `2.1.*` matches `>= 2.1.0, < 2.2.0`.

**Why**: Agents often know the major version but not the exact patch. Wildcard matching reduces friction on download. But inspection tools operating on cached packages need unambiguous version references — there's no sensible behavior for "inspect types in version `2.*`" when multiple versions may be cached.

### 10. Package storage — standard NuGet global cache with safe extraction

**Choice**: Download packages to the standard NuGet global packages folder (`~/.nuget/packages`, or `NUGET_PACKAGES` env var). Use `NuGet.Packaging.PackageExtractor` (or equivalent from the NuGet SDK) to extract packages identically to how `dotnet restore` does it, including hash files (`.nupkg.metadata`, `.nupkg.sha512`).

**Why**: Using the standard location means packages downloaded by SharpRecon are available to `dotnet restore` and vice versa. Using `PackageExtractor` ensures we don't create corrupted cache entries that break unrelated builds. It also handles zip path traversal protection.

**Security**: Only download from nuget.org (hardcoded source URL). Do not accept configurable feed URLs in v1 — this prevents an agent from being tricked into downloading from a malicious feed.

### 11. Concurrency — serialize heavy operations, allow cheap ones

**Choice**: Use a `SemaphoreSlim(1, 1)` to serialize decompilation and assembly inspection operations (anything creating `MetadataLoadContext` or `CSharpDecompiler`). NuGet operations (`nuget_download`, `assembly_list`) and pure-metadata operations are not serialized.

**Why**: The MCP SDK may dispatch concurrent tool calls. Simultaneous large-assembly decompilations could spike memory, and `MetadataLoadContext` file locking on Windows creates conflicts. But serializing cheap operations like `assembly_list` (pure filesystem reads) behind a 30-second decompilation is unnecessary latency. Scoping the semaphore to heavy operations gives the best balance.

### 12. `ref/` vs `lib/` directory selection

**Choice**: Inspection tools (`type_list`, `type_search`, `type_detail`, `member_detail`) prefer `ref/` assemblies when available, falling back to `lib/`. Decompilation tools (`decompile_type`, `decompile_member`) MUST use `lib/` assemblies.

**Why**: Reference assemblies (`ref/`) have cleaner metadata — correct nullability annotations, no method bodies, optimized for API surface inspection. Implementation assemblies (`lib/`) contain method bodies required for decompilation. Many packages (especially Microsoft packages) ship both. Some packages only have `lib/`.

### 13. Targeting pack discovery — cross-platform

**Choice**: Discover targeting packs by resolving the dotnet root directory and scanning `{dotnetRoot}/packs/Microsoft.NETCore.App.Ref/{version}/ref/{tfm}/`. Resolve dotnet root via: (1) `DOTNET_ROOT` environment variable, (2) `Path.GetDirectoryName(RuntimeEnvironment.GetRuntimeDirectory())` walking up to the SDK root, (3) platform-specific defaults (`C:\Program Files\dotnet` on Windows, `/usr/share/dotnet` on Linux, `/usr/local/share/dotnet` on macOS).

**Why**: Targeting pack locations differ across operating systems. Hardcoding paths would break cross-platform support. The priority order handles custom installations and standard installs.

**Known limitation**: When using runtime directory assemblies as fallback (no targeting pack installed), type forwarding chains (`[TypeForwardedTo]`) may not resolve correctly. This is acknowledged and documented — targeting packs are the correct path.

### 14. Packages with `runtimes/` and empty `lib/` directories

**Choice**: When a package has an empty `lib/` folder (containing only `_._` marker files) but has `runtimes/{rid}/lib/{tfm}/` directories, the resolver SHALL look in the `runtimes/` directory for the current platform's RID. Use `RuntimeInformation.RuntimeIdentifier` to determine the current RID.

**Why**: Packages like `System.Runtime.CompilerServices.Unsafe` and `Microsoft.Data.SqlClient` use this pattern for platform-specific assemblies. Without this, inspection of these packages would fail silently.

## Risks / Trade-offs

**[Risk] NuGet API rate limiting or network failures** → Tiers 1 and 2 work offline. Tier 3 failures reported in `UnresolvedDependencies` so the agent knows results may be incomplete. Never throw — degrade gracefully.

**[Risk] Large decompiled output exceeds agent context window** → `maxLength` parameter with truncation marker. `decompile_member` for surgical precision.

**[Risk] MetadataLoadContext can't resolve all types in complex dependency graphs** → Report unresolved dependencies in tool output. Partial results are better than errors.

**[Risk] ICSharpCode.Decompiler memory usage on large assemblies** → Per-request lifecycle + serialized heavy operations. Accept GC pressure.

**[Risk] ICSharpCode.Decompiler may not handle .NET 10 IL** → The latest ICSharpCode.Decompiler (9.1.0, April 2025) may not understand new IL patterns from .NET 10. Decompilation of .NET 10-compiled assemblies may produce incorrect or incomplete output. This is a known limitation — graceful degradation applies. Monitor for new decompiler releases.

**[Risk] Stale targeting pack discovery** → Discover packs at startup, prefer highest version matching requested TFM. Fall back to running runtime directory.

**[Risk] Wildcard version resolution requires network** → No caching of wildcard-to-version resolution. Each wildcard or "latest" request re-queries NuGet. Users may be publishing packages and checking availability, so stale resolution would be actively harmful. Exact version requests can skip resolution since they're unambiguous. The NuGet API latency (~200-500ms) is acceptable for an agent workflow.

**[Risk] Malicious .nupkg zip entries with path traversal** → Using `NuGet.Packaging.PackageExtractor` handles this. Do not implement custom extraction.

**[Risk] MCP SDK 1.0.0 API surface not yet validated** → The `ModelContextProtocol` 1.0.0 NuGet package was published 2026-02-25. The exact tool result types, error handling patterns, and attribute names (`[McpServerToolType]`, `[McpServerTool]`) must be validated against the actual 1.0.0 API during initial project scaffolding. The first implementation task should be a minimal "hello world" MCP server that confirms the SDK patterns before building tools on top.
