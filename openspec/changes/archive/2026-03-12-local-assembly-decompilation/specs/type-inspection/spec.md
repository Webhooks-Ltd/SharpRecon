## ADDED Requirements

### Requirement: Update parameter descriptions for dual-source tools
All tools that accept both NuGet and local sources SHALL update their `packageId` parameter description to `"Package or local load identifier (from nuget_download or local_load)"` and their `version` parameter description to `"Version (from nuget_download or local_load)"`. The `assembly_list` tool description SHALL be updated from "Lists assemblies in a cached NuGet package" to "Lists assemblies in a downloaded package or local load."

#### Scenario: Updated packageId description
- **WHEN** an LLM reads the `decompile_type` tool definition
- **THEN** the `packageId` parameter description reads `"Package or local load identifier (from nuget_download or local_load)"`

#### Scenario: Updated assembly_list tool description
- **WHEN** an LLM reads the `assembly_list` tool definition
- **THEN** the tool description covers both NuGet packages and local loads

### Requirement: Skip version validation for local sources
Tools SHALL skip `ToolHelper.ValidateExactVersion(version)` when `packageId` starts with `local:`. The version for local sources is always the literal string `"local"`, which is not a valid NuGet version and would be rejected by the existing validation.

#### Scenario: Local source bypasses version validation
- **WHEN** a tool is called with `packageId = "local:MyApp"`, `version = "local"`
- **THEN** `ValidateExactVersion` is not called, and the tool proceeds to local resolution

#### Scenario: NuGet source still validated
- **WHEN** a tool is called with `packageId = "Newtonsoft.Json"`, `version = "13.*"`
- **THEN** `ValidateExactVersion` rejects the wildcard version as before

### Requirement: assembly_list TFM handling for local loads
When `assembly_list` is called for a local load, it SHALL list all managed assemblies under the single inferred TFM. If an explicit `tfm` parameter is passed for a local load, it SHALL be ignored (local loads have a single inferred TFM).

#### Scenario: assembly_list for local directory load
- **WHEN** agent calls `assembly_list` with `packageId = "local:Debug/net10.0"`, `version = "local"`
- **THEN** the tool lists all managed assemblies from the directory, grouped under the inferred TFM, same structural layout as NuGet output

#### Scenario: assembly_list with explicit TFM for local load
- **WHEN** agent calls `assembly_list` with `packageId = "local:Debug/net10.0"`, `version = "local"`, `tfm = "net8.0"`
- **THEN** the tool ignores the `tfm` parameter and lists assemblies under the inferred TFM

## MODIFIED Requirements

### Requirement: List public types in an assembly
The `type_list` tool SHALL list all public types exported by an assembly, grouped by namespace, with each type's kind (class, struct, interface, enum, delegate, record*). The tool SHALL accept assemblies from both NuGet packages (via `packageId`/`version`) and locally-loaded assemblies (via synthetic `local:` identifiers from `local_load`). For NuGet packages, the tool SHALL prefer `ref/` assemblies when available. For local assemblies, the tool SHALL use the file directly.

*Record detection is best-effort heuristic — types that cannot be identified as records will render as `class` or `struct`.

#### Scenario: List types with namespace grouping
- **WHEN** agent calls `type_list` for an assembly
- **THEN** the tool returns all public types grouped by namespace, each with its full name and kind

#### Scenario: Filter by namespace
- **WHEN** agent calls `type_list` with a `namespace` parameter
- **THEN** the tool returns only public types in that namespace

#### Scenario: TFM auto-selection
- **WHEN** agent calls `type_list` without specifying a `tfm` parameter
- **THEN** for NuGet packages, the tool selects the highest available `net*` TFM, falling back to highest `netstandard*`, then highest `netcoreapp*`; for local assemblies, the tool uses the TFM inferred during `local_load`

#### Scenario: Explicit TFM selection
- **WHEN** agent calls `type_list` with an explicit `tfm` parameter
- **THEN** the tool uses that exact TFM for assembly resolution

#### Scenario: Prefers ref assemblies for NuGet inspection
- **WHEN** a NuGet package has both `ref/{tfm}/` and `lib/{tfm}/` directories
- **THEN** the tool loads the assembly from `ref/` for metadata inspection (cleaner metadata, correct nullability)

#### Scenario: Local assembly uses file directly
- **WHEN** agent calls `type_list` with a `local:` packageId
- **THEN** the tool loads the assembly directly from the registered file path (no ref/lib distinction)

### Requirement: Search types by name
The `type_search` tool SHALL search for public types by name pattern (substring, case-insensitive) across one or all assemblies in a package or local load. Results SHALL be capped at `maxResults` (default 100) to prevent flooding the agent context window.

#### Scenario: Search across all assemblies
- **WHEN** agent calls `type_search` with query `JsonConvert` and no assemblyName filter
- **THEN** the tool returns all matching types across all assemblies with their full name, kind, and containing assembly

#### Scenario: Search within a single assembly
- **WHEN** agent calls `type_search` with query `JsonConvert` and assemblyName `Newtonsoft.Json`
- **THEN** the tool returns only matching types from that assembly

#### Scenario: No matches found
- **WHEN** agent calls `type_search` with a query that matches no types
- **THEN** the tool returns an empty result set (not an error)

#### Scenario: Results exceed maxResults
- **WHEN** more than `maxResults` types match the query
- **THEN** the tool returns the first `maxResults` matches and includes a note: "Showing {maxResults} of {total} matches. Narrow your query for more specific results."

#### Scenario: Search across local directory load
- **WHEN** agent calls `type_search` with a `local:` packageId from a directory load
- **THEN** the tool searches across all assemblies registered from that directory

### Requirement: Get type detail with XML docs
The `type_detail` tool SHALL return the full type declaration signature, XML doc comments, and all public member signatures for a type. For NuGet packages, the tool SHALL prefer `ref/` assemblies. For local assemblies, the tool SHALL use the file directly and probe for XML docs in the same directory.

#### Scenario: Full type detail
- **WHEN** agent calls `type_detail` for a type
- **THEN** the response includes:
  - Full C# type declaration (access modifier, kind, name, generics, constraints, base type, interfaces)
  - XML doc summary (if available from sidecar `.xml` file)
  - All public members grouped by kind (constructors, methods, properties, fields, events) with their C# signatures

#### Scenario: Generic type with constraints
- **WHEN** agent calls `type_detail` for a generic type like `Dictionary<TKey, TValue>`
- **THEN** the type declaration includes generic parameters and all `where` constraints

#### Scenario: XML docs not available
- **WHEN** the assembly has no sidecar `.xml` file
- **THEN** the tool returns signatures without doc comments (no error)

#### Scenario: Type not found
- **WHEN** agent calls `type_detail` with a type name that does not exist in the assembly
- **THEN** the tool returns a structured error message. If similar type names exist, include suggestions.

#### Scenario: Inherited members excluded by default
- **WHEN** agent calls `type_detail` without `includeInherited` (or `includeInherited = false`)
- **THEN** only members declared directly on the type are returned (inherited members from `System.Object`, `System.Enum`, etc. are excluded)
- **AND** the internal `value__` backing field on enums is never included

#### Scenario: Inherited members included on request
- **WHEN** agent calls `type_detail` with `includeInherited = true`
- **THEN** inherited members from base types are included in the response

#### Scenario: Enum type detail
- **WHEN** agent calls `type_detail` for an enum
- **THEN** the response includes the enum's const fields with XML docs
- **AND** inherited `System.Enum` methods are excluded by default
- **AND** the internal `value__` field is excluded

#### Scenario: Local assembly XML docs
- **WHEN** agent calls `type_detail` for a locally-loaded assembly that has `{assemblyName}.xml` in the same directory
- **THEN** XML doc comments are included in the response

### Requirement: Decompile type to C# source
The `decompile_type` tool SHALL decompile a type back to C# source code using ICSharpCode.Decompiler. For NuGet packages, the tool MUST use `lib/` assemblies (not `ref/`). For local assemblies, the tool SHALL use the file directly.

#### Scenario: Decompile a type
- **WHEN** agent calls `decompile_type` for a fully qualified type name
- **THEN** the tool returns C# source code for the full type including all members and nested types

#### Scenario: Output truncation
- **WHEN** the decompiled source exceeds the `maxLength` parameter (default 50000 characters)
- **THEN** the output is truncated with a clear `// ... output truncated at {limit} characters` marker

#### Scenario: Missing assembly dependencies
- **WHEN** the decompiler cannot resolve all referenced assemblies
- **THEN** the tool returns best-effort C# source with degraded output where types could not be resolved, plus a note listing unresolved dependencies

#### Scenario: .NET 10 IL patterns
- **WHEN** an assembly was compiled with .NET 10 and uses IL patterns the decompiler does not recognize
- **THEN** the tool returns best-effort output (ICSharpCode.Decompiler 9.1.0 may not handle all .NET 10 patterns)

#### Scenario: Mixed-mode assembly warning
- **WHEN** agent decompiles a type from a mixed-mode (C++/CLI) assembly
- **THEN** native method bodies appear as empty stubs and the tool includes a warning: "This is a mixed-mode assembly. Native method bodies cannot be decompiled."

### Requirement: Decompile member to C# source
The `decompile_member` tool SHALL decompile a specific member of a type to C# source code. For NuGet packages, the tool MUST use `lib/` assemblies. For local assemblies, the tool SHALL use the file directly.

#### Scenario: Decompile a single method
- **WHEN** agent calls `decompile_member` for a method name
- **THEN** the tool returns C# source code for that method only

#### Scenario: Disambiguate overloads
- **WHEN** agent calls `decompile_member` with `parameterTypes` for an overloaded method
- **THEN** the tool returns source code for the specific overload matching those parameter types

#### Scenario: No parameter types for overloaded method
- **WHEN** agent calls `decompile_member` for an overloaded method without `parameterTypes`
- **THEN** the tool returns source code for all overloads of that member

#### Scenario: Invalid parameter type format
- **WHEN** agent passes C# keyword names (e.g. `string` instead of `System.String`) in `parameterTypes`
- **THEN** the tool returns a structured error message: "Use fully qualified CLR type names in parameterTypes (e.g. 'System.String', not 'string')"

### Requirement: Exception safety for all inspection tools
All type inspection and decompilation tools SHALL catch all exceptions and return structured MCP error responses with actionable messages. No exception SHALL propagate to stdout.

#### Scenario: Assembly load failure
- **WHEN** an assembly cannot be loaded by MetadataLoadContext or the decompiler
- **THEN** the tool returns a structured error message without corrupting the MCP stdio connection

#### Scenario: Out of memory during decompilation
- **WHEN** decompilation of a very large type exhausts available memory
- **THEN** the tool catches the exception and returns a structured error: "Type too large to decompile in full. Use decompile_member to decompile individual members."

#### Scenario: Package not in cache
- **WHEN** an inspection tool is called for a NuGet package not in the cache
- **THEN** the tool returns: "Package '{packageId}' version '{version}' not found in cache. Call nuget_download first."

#### Scenario: Local assembly not registered
- **WHEN** an inspection tool is called with a `local:` packageId that has not been loaded
- **THEN** the tool returns: "Local assembly '{name}' not loaded. Call local_load first."
