# type-inspection Specification

## Purpose
TBD - created by archiving change initial-mcp-server. Update Purpose after archive.
## Requirements
### Requirement: List public types in an assembly
The `type_list` tool SHALL list all public types exported by an assembly in a cached NuGet package, grouped by namespace, with each type's kind (class, struct, interface, enum, delegate, record*). The tool SHALL accept exact versions only (not wildcards). For inspection, the tool SHALL prefer `ref/` assemblies when available, falling back to `lib/`.

*Record detection is best-effort heuristic — types that cannot be identified as records will render as `class` or `struct`.

#### Scenario: List types with namespace grouping
- **WHEN** agent calls `type_list` for an assembly
- **THEN** the tool returns all public types grouped by namespace, each with its full name and kind

#### Scenario: Filter by namespace
- **WHEN** agent calls `type_list` with a `namespace` parameter
- **THEN** the tool returns only public types in that namespace

#### Scenario: TFM auto-selection
- **WHEN** agent calls `type_list` without specifying a `tfm` parameter
- **THEN** the tool selects the highest available `net*` TFM, falling back to highest `netstandard*`, then highest `netcoreapp*`

#### Scenario: Explicit TFM selection
- **WHEN** agent calls `type_list` with an explicit `tfm` parameter
- **THEN** the tool uses that exact TFM for assembly resolution

#### Scenario: Prefers ref assemblies for inspection
- **WHEN** a package has both `ref/{tfm}/` and `lib/{tfm}/` directories
- **THEN** the tool loads the assembly from `ref/` for metadata inspection (cleaner metadata, correct nullability)

### Requirement: Search types by name
The `type_search` tool SHALL search for public types by name pattern (substring, case-insensitive) across one or all assemblies in a package. Results SHALL be capped at `maxResults` (default 100) to prevent flooding the agent context window.

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

### Requirement: Get type detail with XML docs
The `type_detail` tool SHALL return the full type declaration signature, XML doc comments, and all public member signatures for a type. For inspection, the tool SHALL prefer `ref/` assemblies when available.

#### Scenario: Full type detail
- **WHEN** agent calls `type_detail` for `Newtonsoft.Json.JsonConvert`
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

### Requirement: Get member detail with XML docs
The `member_detail` tool SHALL return full overload signatures and XML doc comments for a specific member of a type. The tool SHALL accept an optional `parameterTypes` parameter (fully qualified CLR type names, e.g. `["System.Object", "System.String"]`) to filter to a specific overload. Use `".ctor"` as `memberName` for constructors.

#### Scenario: Method with multiple overloads (no filter)
- **WHEN** agent calls `member_detail` for `SerializeObject` on `Newtonsoft.Json.JsonConvert` without `parameterTypes`
- **THEN** the tool returns all overloads, each with:
  - Full C# signature (return type, method name, generic parameters, parameters with types/names/modifiers/defaults)
  - XML doc comment (summary, param descriptions, returns, exceptions, remarks)

#### Scenario: Filter to specific overload
- **WHEN** agent calls `member_detail` for `SerializeObject` with `parameterTypes: ["System.Object", "System.Type"]`
- **THEN** the tool returns only the overload matching those parameter types

#### Scenario: Constructor lookup
- **WHEN** agent calls `member_detail` with memberName `".ctor"`
- **THEN** the tool returns all public constructor overloads with their signatures and XML docs

#### Scenario: Property detail
- **WHEN** agent calls `member_detail` for a property
- **THEN** the response includes the property type, name, accessors (get/set/init), and XML docs

#### Scenario: Member not found
- **WHEN** agent calls `member_detail` with a member name that does not exist on the type
- **THEN** the tool returns a structured error message listing available members of the type

#### Scenario: Invalid parameterTypes format
- **WHEN** agent passes C# keyword names (e.g. `"string"` instead of `"System.String"`) in `parameterTypes`
- **THEN** the tool returns a structured error: "Use fully qualified CLR type names in parameterTypes (e.g. 'System.String', not 'string')"

### Requirement: Signature rendering fidelity
All type and member signatures SHALL be rendered as valid C# declarations, including:
- Access modifiers (`public`, `protected`)
- Type kind keywords (`class`, `struct`, `interface`, `enum`, `delegate`; `record` as best-effort heuristic)
- Modifiers (`static`, `sealed`, `abstract`, `readonly`, `virtual`, `override`)
- Generic type parameters and `where` constraints
- Nullability annotations (`?`) — requires reading `NullableAttribute`/`NullableContextAttribute` byte encoding. This is the most complex part of signature rendering and SHALL have extensive test coverage.
- Parameter modifiers (`ref`, `out`, `in`, `params`, `scoped`)
- Default parameter values
- Base type and implemented interfaces (for type declarations)
- Return types (for methods and properties)

The `async` keyword SHALL NOT be rendered — it is not present in assembly metadata and cannot be reliably inferred. Methods returning `Task<T>` or `ValueTask<T>` are not necessarily `async`.

#### Scenario: Complex generic method signature
- **WHEN** a method has generic parameters with multiple constraints
- **THEN** the rendered signature includes all constraints in valid C# syntax, e.g. `public T Deserialize<T>(string json) where T : class, new()`

#### Scenario: Nullable reference type parameters
- **WHEN** a method has nullable reference type parameters
- **THEN** the rendered signature includes `?` annotations, e.g. `public static string? Serialize(object? value)`

#### Scenario: Default parameter values
- **WHEN** a method has parameters with default values
- **THEN** the rendered signature includes the defaults, e.g. `public void Log(string message, LogLevel level = LogLevel.Information)`

#### Scenario: Nullability not available
- **WHEN** an assembly was compiled without nullable annotations (no `NullableContextAttribute`)
- **THEN** the rendered signature omits `?` annotations (no guessing)

### Requirement: Decompile type to C# source
The `decompile_type` tool SHALL decompile a type back to C# source code using ICSharpCode.Decompiler. The tool MUST use `lib/` assemblies (not `ref/`) since reference assemblies have no method bodies.

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

### Requirement: Decompile member to C# source
The `decompile_member` tool SHALL decompile a specific member of a type to C# source code. The tool MUST use `lib/` assemblies.

#### Scenario: Decompile a single method
- **WHEN** agent calls `decompile_member` for a method name
- **THEN** the tool returns C# source code for that method only

#### Scenario: Disambiguate overloads
- **WHEN** agent calls `decompile_member` with `parameterTypes` (fully qualified CLR type names, e.g. `["System.String", "System.Int32"]`) for an overloaded method
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
- **WHEN** an inspection tool is called for a package not in the global cache
- **THEN** the tool returns: "Package '{packageId}' version '{version}' not found in cache. Call nuget_download first."

