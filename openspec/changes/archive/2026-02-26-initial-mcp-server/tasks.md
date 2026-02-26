## 1. Project Scaffolding & SDK Validation

- [x] 1.1 Create `SharpRecon.sln`, `Directory.Build.props`, and `src/SharpRecon/SharpRecon.csproj` targeting net10.0 with all NuGet dependencies (`ModelContextProtocol` 1.0.0, `Microsoft.Extensions.Hosting`, `NuGet.Protocol`, `NuGet.Packaging`, `NuGet.Frameworks`, `ICSharpCode.Decompiler`)
- [x] 1.2 Create `Program.cs` with Generic Host builder, MCP server registration (`AddMcpServer`, `WithStdioServerTransport`, `WithToolsFromAssembly`), and stderr-only logging
- [x] 1.3 Create a minimal "hello world" `[McpServerTool]` to validate the 1.0.0 SDK API surface — confirm tool registration, tool result types, error result pattern (`isError: true`), and attribute names. Document findings in a `NOTES.md` for reference during implementation.
- [x] 1.4 Create `tests/SharpRecon.Tests/SharpRecon.Tests.csproj` with test framework (xUnit v3 + Shouldly + NSubstitute + AutoFixture + AutoFixture.AutoNSubstitute)
- [x] 1.5 Add `.gitignore`, initial commit

## 2. Infrastructure — Package Cache & NuGet Global Cache

- [x] 2.1 Implement `IPackageCache` interface and `PackageCache` class: resolve global packages path (`NUGET_PACKAGES` env var or `~/.nuget/packages`), `GetPackagePath(packageId, version)`, `IsPackageCached(packageId, version)`, `GetAssemblyPath(packageId, version, tfm, assemblyName)` with `ref/` vs `lib/` preference support
- [x] 2.2 Implement `runtimes/{rid}/lib/{tfm}/` fallback in `PackageCache` for packages with empty `lib/` (`_._` markers)
- [x] 2.3 Write unit tests for `PackageCache` using temp directories with known package layouts (including `ref/`, `lib/`, `runtimes/`, `_._` markers)

## 3. Infrastructure — Framework Assembly Resolution

- [x] 3.1 Implement `FrameworkAssemblyResolver`: cross-platform targeting pack discovery via `DOTNET_ROOT`, `RuntimeEnvironment.GetRuntimeDirectory()` walk-up, and platform defaults. Scan `{dotnetRoot}/packs/Microsoft.NETCore.App.Ref/{version}/ref/{tfm}/` at startup.
- [x] 3.2 Implement `GetFrameworkAssemblyPaths(tfm)`: exact TFM match → netstandard fallback → running runtime fallback
- [x] 3.3 Write tests for `FrameworkAssemblyResolver` verifying it finds targeting packs on the current machine

## 4. Infrastructure — NuGet Dependency Resolution

- [x] 4.1 Implement `.nuspec` reading: given a cached package path, parse the `.nuspec` to extract declared dependency package IDs and version ranges for a given TFM
- [x] 4.2 Implement `GlobalCacheAssemblyResolver`: look up dependency packages by package ID in global cache, use `FrameworkReducer.GetNearest()` for TFM compatibility matching
- [x] 4.3 Implement `NuGetDependencyResolver`: resolve transitive dependency graph via `NuGet.Protocol` `DependencyInfoResource`, depth limit of 6, per-package 10s timeout, graceful degradation on failures
- [x] 4.4 Implement `NuGetDependencyResolver.EnsurePackageDownloadedAsync`: download missing packages using `PackageExtractor`-equivalent safe extraction (including `.nupkg.metadata`, `.nupkg.sha512`)
- [x] 4.5 Implement `AssemblyPathResolver` (composite): orchestrate tiers 1→2→3, return `AssemblyResolutionResult` with primary path, framework paths, dependency paths, unresolved list. Cache results per `(packageId, version, tfm)`.
- [x] 4.6 Write integration tests for `AssemblyPathResolver` against a real small package (e.g. `Newtonsoft.Json`)

## 5. NuGet Tools

- [x] 5.1 Implement `INuGetService` interface: `DownloadPackageAsync(packageId, version)` with wildcard/latest resolution, `GetAvailableTfms(packageId, version)`, `ListAssemblies(packageId, version, tfm?)`
- [x] 5.2 Implement `NuGetService`: wildcard version parsing (`2.*` → `>= 2.0.0, < 3.0.0`), latest stable resolution via `PackageMetadataResource`, download via `DownloadResource` with `PackageExtractor`, nuget.org-only hardcoded source
- [x] 5.3 Implement `NuGetDownloadTool` (`nuget_download`): parameter validation, wildcard/latest/exact dispatch, error handling with actionable messages, returns resolved version + TFMs
- [x] 5.4 Implement `AssemblyListTool` (`assembly_list`): exact version only, TFM filter, `ref/` + `lib/` + `runtimes/` scanning, error messages listing available TFMs when TFM not found
- [x] 5.5 Write tests for `NuGetService` wildcard version parsing and TFM listing logic (unit tests with mocked NuGet resources for network-dependent parts)

## 6. XML Doc Parser

- [x] 6.1 Implement `XmlDocParser` as singleton: parse `.xml` sidecar files, `ConcurrentDictionary` cache keyed by `"{packageId}/{version}/{tfm}/{assemblyName}"`, resolve XML doc member ID strings
- [x] 6.2 Implement `XmlDocCollection`: lookup by member XML doc ID (`M:`, `T:`, `P:`, `F:`, `E:` prefixes), return structured doc (summary, params, returns, exceptions, remarks)
- [x] 6.3 Write unit tests for `XmlDocParser` against fixture XML files covering methods, types, properties, generic types, overloaded methods

## 7. Signature Rendering — TypeRenderer

- [x] 7.1 Implement `TypeRenderer.RenderTypeDeclaration`: access modifiers, kind keyword (class/struct/interface/enum/delegate), name, generics, constraints, base type, interfaces
- [x] 7.2 Implement record detection heuristic: look for compiler-synthesized `PrintMembers`, `EqualityContract`, clone method. Best-effort, falls back to `class`/`struct`.
- [x] 7.3 Implement `TypeRenderer.RenderMethodSignature`: return type, name, generic params, parameters with types/names/modifiers (`ref`/`out`/`in`/`params`/`scoped`), default values, constraints. No `async` keyword.
- [x] 7.4 Implement `TypeRenderer.RenderPropertySignature`: type, name, accessors (get/set/init)
- [x] 7.5 Implement nullability annotation rendering: read `NullableAttribute` and `NullableContextAttribute` byte encoding, handle oblivious/not-annotated/annotated states, generic type tree flattening. Skip when attributes not present.
- [x] 7.6 Write extensive tests for `TypeRenderer` against known assemblies via `MetadataLoadContext`: simple types, generics with constraints, nullable annotations, default params, ref/out/in/params, records, enums, delegates, nested types

## 8. Assembly Inspection — IAssemblyInspector

- [x] 8.1 Implement `IAssemblyInspector` interface: `GetTypes(packageId, version, tfm, assemblyName, namespace?)`, `SearchTypes(packageId, version, tfm, assemblyName?, query, maxResults)`, `GetTypeDetail(packageId, version, tfm, assemblyName?, typeName)`, `GetMemberDetail(packageId, version, tfm, assemblyName?, typeName, memberName, parameterTypes?)`
- [x] 8.2 Implement `AssemblyInspector`: create `MetadataLoadContext` per operation using `AssemblyPathResolver` for dependency resolution, prefer `ref/` assemblies via `IPackageCache`, integrate `XmlDocParser` and `TypeRenderer` for results
- [x] 8.3 Implement `MetadataLoadContextFactory`: accept `AssemblyResolutionResult`, create `PathAssemblyResolver` from all resolved paths, return `MetadataLoadContext`
- [x] 8.4 Write integration tests for `AssemblyInspector` against a real package (e.g. `Newtonsoft.Json`) — type listing, search, type detail with generics and XML docs, member detail with overloads

## 9. Inspection MCP Tools

- [x] 9.1 Implement shared tool exception handling helper: catch all exceptions, return MCP error results with actionable messages, log to stderr
- [x] 9.2 Implement concurrency semaphore for heavy operations (inspection + decompilation) — `SemaphoreSlim(1, 1)` scoped to heavy tools only
- [x] 9.3 Implement `TypeListTool` (`type_list`): exact version validation, TFM auto-selection (highest net* → netstandard* → netcoreapp*), namespace filter, delegates to `IAssemblyInspector`
- [x] 9.4 Implement `TypeSearchTool` (`type_search`): maxResults (default 100), truncation note when exceeding limit
- [x] 9.5 Implement `TypeDetailTool` (`type_detail`): assemblyName hint, full type declaration + XML docs + members grouped by kind
- [x] 9.6 Implement `MemberDetailTool` (`member_detail`): all overloads with signatures + XML docs, optional `parameterTypes` filter (fully qualified CLR names, validate format), `.ctor` support for constructors, suggest available members on not-found

## 10. Decompilation

- [x] 10.1 Implement `AssemblyDecompiler`: create `CSharpDecompiler` per call with `DecompilerSettings(LanguageVersion.Latest)` and `ThrowOnAssemblyResolveErrors = false`, use `UniversalAssemblyResolver` with dependency search directories from `AssemblyPathResolver`, `Task.Run` wrapper for CPU offloading
- [x] 10.2 Implement `DecompileTypeTool` (`decompile_type`): must use `lib/` assemblies (not `ref/`), `maxLength` truncation with marker, report unresolved dependencies
- [x] 10.3 Implement `DecompileMemberTool` (`decompile_member`): resolve member by name + optional `parameterTypes` (fully qualified CLR names), validate parameterTypes format, return all overloads when no disambiguation
- [x] 10.4 Write integration tests for decompilation against a real package — decompile a known type and verify output contains expected method bodies

## 11. End-to-End Smoke Testing

- [x] 11.1 Manual smoke test: run server, configure as MCP tool in Claude Code, execute full workflow: `nuget_download` Newtonsoft.Json → `assembly_list` → `type_search` JsonConvert → `type_detail` → `member_detail` SerializeObject → `decompile_type` → `decompile_member`
- [x] 11.2 Verify exception safety: call tools with invalid inputs (bad package name, bad version, bad type name, bad TFM) and confirm structured error responses, no stdout corruption
- [x] 11.3 Verify wildcard version: `nuget_download` with `13.*`, confirm resolved version, then inspect with exact version
- [x] 11.4 Verify dependency resolution: inspect a package with transitive dependencies (e.g. `Microsoft.Extensions.DependencyInjection`), confirm base types from dependency assemblies resolve correctly
