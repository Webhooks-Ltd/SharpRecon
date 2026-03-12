## ADDED Requirements

### Requirement: Load a local assembly by file path
The `local_load` tool SHALL accept a file path to a `.dll` or `.exe` and register it for use with all inspection and decompilation tools. The tool SHALL return structured output including synthetic identifiers (`packageId` with `local:` prefix, `version = "local"`), the inferred TFM, assembly count, and next-step guidance. The tool SHALL normalise relative paths via `Path.GetFullPath`.

#### Scenario: Load a single DLL
- **WHEN** agent calls `local_load` with `path = "C:/projects/MyApp/bin/Debug/net10.0/MyApp.dll"`
- **THEN** the tool registers the assembly and returns output:
  ```
  Source: local:MyApp
  Version: local
  Path: C:/projects/MyApp/bin/Debug/net10.0/MyApp.dll
  Inferred TFM: net10.0
  Assemblies loaded: 1

  Next: use assembly_list, type_list, type_search, or decompile_type with packageId="local:MyApp" version="local"
  ```

#### Scenario: Load a single EXE
- **WHEN** agent calls `local_load` with `path = "C:/tools/mytool.exe"` pointing to a managed .NET executable
- **THEN** the tool registers the assembly and returns structured output with synthetic identifiers, same format as for a DLL

#### Scenario: Relative path normalisation
- **WHEN** agent calls `local_load` with `path = "../bin/Debug/net10.0/MyApp.dll"`
- **THEN** the tool resolves the path to an absolute path via `Path.GetFullPath` and registers it

#### Scenario: Re-loading an already loaded path
- **WHEN** agent calls `local_load` with a path that was previously loaded
- **THEN** the tool replaces the previous registration (to pick up rebuilt assemblies), invalidates any cached resolution results, and returns the same synthetic identifiers

### Requirement: Load assemblies from a directory
The `local_load` tool SHALL accept a directory path and register all managed `.dll` and `.exe` files found in that directory (non-recursive scan). The synthetic `packageId` SHALL be `local:{parentDirName}/{dirName}` to reduce naming collisions across projects.

#### Scenario: Load a build output directory
- **WHEN** agent calls `local_load` with `path = "C:/projects/MyApp/bin/Debug/net10.0/"`
- **THEN** the tool scans the directory, registers managed assemblies, and returns output:
  ```
  Source: local:Debug/net10.0
  Version: local
  Path: C:/projects/MyApp/bin/Debug/net10.0/
  Inferred TFM: net10.0
  Assemblies loaded: 12 (3 native DLLs skipped)

  Next: use assembly_list, type_list, type_search, or decompile_type with packageId="local:Debug/net10.0" version="local"
  ```

#### Scenario: Empty directory (no managed assemblies)
- **WHEN** agent calls `local_load` with a directory where all `.dll`/`.exe` files are native (or none exist)
- **THEN** the tool returns an error: "No .NET assemblies found in directory: {path}"

#### Scenario: Directory with non-.NET files mixed in
- **WHEN** the directory contains `.json`, `.xml`, `.pdb`, and `.dll` files
- **THEN** only managed `.dll` and `.exe` files are registered; other files are ignored (but `.xml` files are probed for XML docs)

#### Scenario: Directory with mixed managed and native DLLs
- **WHEN** agent calls `local_load` with a directory containing both managed and native `.dll` files (e.g., `e_sqlite3.dll`, `libSkiaSharp.dll` alongside `MyApp.dll`)
- **THEN** managed assemblies are registered, native DLLs are silently skipped, and the output reports: "{N} assemblies loaded ({M} native DLLs skipped)"

#### Scenario: Directory with apphost stub .exe
- **WHEN** a directory contains both `MyApp.exe` (native apphost stub) and `MyApp.dll` (managed assembly)
- **THEN** the `.exe` is skipped (the managed assembly is the `.dll`), and only `MyApp.dll` is registered

#### Scenario: Colliding directory names
- **WHEN** agent calls `local_load` for `C:/projectA/bin/Debug/net10.0/` (producing `local:Debug/net10.0`) and then calls `local_load` for `C:/projectB/bin/Debug/net10.0/`
- **THEN** the second load replaces the first registration. The agent must be aware that only the most recent load with a given synthetic ID is active.

### Requirement: Validate assembly format before loading
The `local_load` tool SHALL validate that the target file is a managed .NET assembly before registering it.

#### Scenario: Native DLL (no CLR header)
- **WHEN** agent calls `local_load` with a path to a native DLL (e.g., `sqlite3.dll`)
- **THEN** the tool checks `PEHeaders.CorHeader is null` and returns an error: "Not a managed .NET assembly: sqlite3.dll"

#### Scenario: Single-file published assembly
- **WHEN** agent calls `local_load` with a path to a single-file published .NET executable
- **THEN** the tool detects the bundle signature (`0xd0e52d67b9e2400a8e4186f9b54d8352` in the last ~4KB of the file) and returns an error: "Single-file published assemblies are not supported. Extract the bundle first."

#### Scenario: File does not exist
- **WHEN** agent calls `local_load` with a path that does not exist
- **THEN** the tool returns an error: "Path not found: {path}"

#### Scenario: Directory does not exist
- **WHEN** agent calls `local_load` with a directory path that does not exist
- **THEN** the tool returns an error: "Path not found: {path}"

### Requirement: Infer TFM from assembly metadata
The `local_load` tool SHALL infer the target framework for use in framework assembly resolution.

#### Scenario: runtimeconfig.json present
- **WHEN** `MyApp.runtimeconfig.json` exists alongside the loaded assembly with `runtimeOptions.framework.name = "Microsoft.NETCore.App"` and `runtimeOptions.framework.version = "10.0.0"`
- **THEN** the tool derives TFM `net10.0` from the framework name and major version

#### Scenario: Assembly with TargetFrameworkAttribute (no runtimeconfig)
- **WHEN** the loaded assembly has `[assembly: TargetFramework(".NETCoreApp,Version=v10.0")]` and no `.runtimeconfig.json` exists
- **THEN** the tool infers `net10.0` from the attribute

#### Scenario: Directory load TFM priority
- **WHEN** a directory contains multiple assemblies targeting different TFMs
- **THEN** the tool uses TFM from: (1) `.runtimeconfig.json` if present, (2) `TargetFrameworkAttribute` from the `.exe` if one exists, (3) `TargetFrameworkAttribute` from the `.dll` matching the directory name, (4) first `.dll` alphabetically

#### Scenario: Assembly without TargetFrameworkAttribute or runtimeconfig
- **WHEN** the loaded assembly has no `TargetFrameworkAttribute` and no `.runtimeconfig.json` exists
- **THEN** the tool defaults to the server's running TFM and includes a warning: "Could not determine target framework. Defaulting to {tfm}."

### Requirement: Synthetic identifiers work transparently with existing tools
Locally-loaded assemblies SHALL be usable with all existing inspection and decompilation tools by passing the synthetic `packageId` and `version` returned by `local_load`. No tool parameter types or names are changed.

#### Scenario: type_list with local assembly
- **WHEN** agent calls `type_list` with `packageId = "local:MyApp"`, `version = "local"`
- **THEN** the tool lists all public types in the locally-loaded assembly, same output format as NuGet packages

#### Scenario: decompile_type with local assembly
- **WHEN** agent calls `decompile_type` with `packageId = "local:MyApp"`, `version = "local"`, `typeName = "MyApp.Services.UserService"`
- **THEN** the tool decompiles the type from the local assembly file, same output format as NuGet packages

#### Scenario: assembly_list with local directory load
- **WHEN** agent calls `assembly_list` with `packageId = "local:Debug/net10.0"`, `version = "local"`
- **THEN** the tool lists all managed assemblies registered from that directory load, grouped under the single inferred TFM

#### Scenario: nuget_search and nuget_download remain NuGet-only
- **WHEN** agent calls `nuget_search` or `nuget_download`
- **THEN** these tools operate against NuGet sources only; local assemblies are not involved

### Requirement: XML documentation discovery for local assemblies
The `local_load` registration SHALL probe for XML documentation files (`{assemblyName}.xml`) in the same directory as each loaded assembly.

#### Scenario: XML docs present
- **WHEN** `MyApp.dll` and `MyApp.xml` are in the same directory
- **THEN** `type_detail` and `member_detail` include XML doc comments from `MyApp.xml`

#### Scenario: XML docs not present
- **WHEN** `MyApp.dll` exists but `MyApp.xml` does not
- **THEN** inspection tools return signatures without doc comments (no error)
