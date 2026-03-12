## 1. IAssemblySource Abstraction

- [x] 1.1 Define `IAssemblySource` interface in `Infrastructure/` with methods: `IsRegistered`, `GetAvailableTfms`, `GetAssembliesForTfm`, `GetAssemblyPath`, `GetXmlDocPath`
- [x] 1.2 Implement `NuGetAssemblySource` wrapping `IPackageCache` — all methods delegate to existing `IPackageCache` methods
- [x] 1.3 Implement `LocalAssemblySource` wrapping `LocalAssemblyRegistry` — `GetAvailableTfms` returns single inferred TFM, `GetAssemblyPath` ignores `preferRef`, `GetXmlDocPath` returns path from registry
- [x] 1.4 Implement `CompositeAssemblySource` that routes to `NuGetAssemblySource` or `LocalAssemblySource` based on `local:` prefix
- [x] 1.5 Refactor `AssemblyInspector` to depend on `IAssemblySource` instead of `IPackageCache` — update all 5 call sites (`SearchTypesAsync`, `ResolveTypeAsync`, `SelectBestTfm`, `ResolveXmlDocPath`, constructor)
- [x] 1.6 Refactor `AssemblyDecompiler` to depend on `IAssemblySource` instead of `IPackageCache` — update `ResolveAssemblyForTypeAsync`
- [x] 1.7 Update DI registration in `Program.cs` to wire `IAssemblySource` → `CompositeAssemblySource` with both source implementations
- [x] 1.8 Verify all existing tests pass after refactor (no behaviour change)

## 2. LocalAssemblyRegistry

- [x] 2.1 Create `LocalAssemblyRegistry` class in `Infrastructure/` with `ConcurrentDictionary<string, LocalRegistration>` keyed by synthetic `packageId`
- [x] 2.2 Define `LocalRegistration` record: `PrimaryPath`, `AssemblyPaths`, `XmlDocPaths` (dictionary), `InferredTfm`, `DepsJsonPath`
- [x] 2.3 Implement `Register` method: accepts file or directory path, scans for managed assemblies, returns synthetic `packageId`
- [x] 2.4 Implement directory scanning: non-recursive, filter managed vs native via CLR header check, skip `.exe` when matching `.dll` exists
- [x] 2.5 Implement synthetic ID generation: `local:{assemblyName}` for files, `local:{parentDir}/{dirName}` for directories
- [x] 2.6 Implement XML doc probing: scan same directory for `{assemblyName}.xml` per registered assembly
- [x] 2.7 Implement re-registration: replace existing entry for same synthetic ID, return same identifiers

## 3. Assembly Validation

- [x] 3.1 Implement CLR header check: `PEHeaders.CorHeader is null` → native DLL error
- [x] 3.2 Implement single-file bundle detection: search last ~4KB for signature `0xd0e52d67b9e2400a8e4186f9b54d8352`
- [x] 3.3 Implement mixed-mode detection: `CorHeader is not null && !CorHeader.Flags.HasFlag(CorFlags.ILOnly)` — store flag on registration for later warning injection
- [x] 3.4 Implement path validation: `Path.GetFullPath` normalisation, `File.Exists`/`Directory.Exists` checks
- [x] 3.5 Write unit tests for all validation scenarios: native DLL, single-file bundle, mixed-mode, missing path, empty directory

## 4. TFM Inference

- [x] 4.1 Implement `.runtimeconfig.json` parsing: read `runtimeOptions.framework.name` + `runtimeOptions.framework.version`, derive TFM (e.g., `Microsoft.NETCore.App` + `10.0.0` → `net10.0`). Handle `runtimeOptions.frameworks[]` array format.
- [x] 4.2 Implement `TargetFrameworkAttribute` reading from assembly metadata via `MetadataLoadContext`
- [x] 4.3 Implement TFM priority for directory loads: (1) `.runtimeconfig.json`, (2) `.exe` attribute, (3) `.dll` matching directory name, (4) first `.dll` alphabetically
- [x] 4.4 Implement fallback: default to server's running TFM with warning when neither source is available
- [x] 4.5 Write unit tests for TFM inference scenarios

## 5. Local Assembly Resolution

- [x] 5.1 Add `ResolveLocalAsync` method to `AssemblyPathResolver` — separate from existing NuGet `ResolveAsync`, accepts primary path, inferred TFM, deps.json path, sibling paths
- [x] 5.2 Implement framework assembly resolution tier: use existing `FrameworkAssemblyResolver` with inferred TFM
- [x] 5.3 Implement sibling directory probing tier: scan assembly's directory for matching `.dll` by assembly name
- [x] 5.4 Implement graceful fallback: report unresolved dependencies in `AssemblyResolutionResult`
- [x] 5.5 Add separate local resolution cache (`ConcurrentDictionary<string, AssemblyResolutionResult>` keyed by absolute path)
- [x] 5.6 Implement cache invalidation on re-registration from `LocalAssemblyRegistry`
- [x] 5.7 Write unit tests for local resolution scenarios

## 6. `.deps.json` Resolution

- [x] 6.1 Implement `.deps.json` parser using `System.Text.Json`: read `runtimeTarget`, `targets`, and `libraries` fields
- [x] 6.2 Map dependency entries to file paths: output directory, NuGet global cache, shared framework store
- [x] 6.3 Integrate as tier 1 in `ResolveLocalAsync` (before framework and directory probing)
- [x] 6.4 Write unit tests with sample `.deps.json` files

## 7. `local_load` MCP Tool

- [x] 7.1 Create `LocalLoadTool` class in `Infrastructure/` (or new `Local/` directory) with `[McpServerToolType]`
- [x] 7.2 Implement tool method: `path` parameter, delegates to `LocalAssemblyRegistry.Register`, returns structured output with synthetic IDs, TFM, assembly count, and "Next:" guidance
- [x] 7.3 Format output for directory loads with native DLL skip count: "{N} assemblies loaded ({M} native DLLs skipped)"
- [x] 7.4 Wire error handling via `ToolHelper.ExecuteWithSemaphoreAsync` pattern
- [x] 7.5 Write integration tests for single file, directory, error cases

## 8. Tool Updates

- [x] 8.1 Update `packageId` descriptions on all tools: `"Package or local load identifier (from nuget_download or local_load)"`
- [x] 8.2 Update `version` descriptions on all tools: `"Version (from nuget_download or local_load)"`
- [x] 8.3 Update `assembly_list` tool description to cover both NuGet and local sources
- [x] 8.4 Add `local:` prefix check in each tool to skip `ValidateExactVersion` for local sources
- [x] 8.5 Update `assembly_list` to handle local loads: list managed assemblies under single inferred TFM, ignore explicit `tfm` param for local sources
- [x] 8.6 Inject mixed-mode assembly warning into decompilation tool output when flag is set on registration
- [x] 8.7 Write integration tests: existing tools with `local:` synthetic IDs for type_list, type_detail, decompile_type, decompile_member

## 9. Documentation and Publish

- [x] 9.1 Update `README.md` with `local_load` tool documentation and example workflow
- [x] 9.2 Update `CHANGELOG.md` under `[Unreleased]`
- [x] 9.3 Run `dotnet publish src/SharpRecon/SharpRecon.csproj -o src/SharpRecon/bin/publish` and verify MCP server starts
- [x] 9.4 End-to-end smoke test: `local_load` a build output directory → `type_list` → `type_detail` → `decompile_type`
