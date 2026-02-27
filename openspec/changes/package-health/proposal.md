## Why

The `nuget_download` tool queries `IPackageSearchMetadata` during wildcard/latest version resolution but discards deprecation status, vulnerability advisories, publish date, and prefix-reserved status. For exact-version downloads, it skips the metadata query entirely. This means an AI agent can recommend a deprecated or vulnerable package with no warning. Adding a lightweight metadata query and surfacing health fields in the existing download response — no new tools, no new dependencies — gives agents the safety signals they need to make sound recommendations.

## What Changes

- Enrich `nuget_download` output with package health metadata from `IPackageSearchMetadata` (NuGet.Protocol 7.3.0):
  - **Deprecation**: status flag, reasons (e.g. `Legacy`, `CriticalBugs`), message, and alternate package ID + version range (from `GetDeprecationMetadataAsync()`)
  - **Vulnerabilities**: list of known CVEs with severity level mapped from int (0=Low, 1=Moderate, 2=High, 3=Critical) and advisory URL (from `Vulnerabilities` property)
  - **Publish date**: when the resolved version was published (from `Published` property)
  - **Verified owner**: whether the package has a reserved prefix on nuget.org (from `PrefixReserved` property)
- Add a `GetPackageHealthAsync` method to `NuGetService` that queries `PackageMetadataResource` and returns a new `PackageHealthInfo` record containing only the formatted fields above — not the raw `IPackageSearchMetadata` SDK type
- The tool layer calls `GetPackageHealthAsync` after `DownloadPackageAsync`, keeping the download path clean and the health query explicit
- Always emit a health section in tool output: explicit "No deprecation notices. No known vulnerabilities." for healthy packages, detailed warnings for unhealthy ones — so the LLM sees a positive safety signal, not ambiguous absence
- Health metadata is best-effort: if the metadata query fails, the download still succeeds and the health section is omitted with a note
- Always query metadata, even for exact-version downloads — one additional HTTP call (~200ms) is acceptable for safety-critical information; no optional parameter to complicate tool selection

## Capabilities

### New Capabilities

_(none)_

### Modified Capabilities

- `nuget-operations`: The `nuget_download` tool response includes package health metadata (deprecation status, vulnerability advisories, publish date, verified owner) alongside version, cache path, and TFMs. One metadata query is added for exact-version downloads; wildcard/latest resolution reuses the metadata already fetched.

## Impact

- **Modified files**:
  - `src/SharpRecon/NuGet/NuGetService.cs` — add `GetPackageHealthAsync` method returning a new `PackageHealthInfo` record; reuse metadata from wildcard resolution when available
  - `src/SharpRecon/NuGet/INuGetService.cs` — add `GetPackageHealthAsync` to interface; add `PackageHealthInfo` record with own types (not NuGet SDK types)
  - `src/SharpRecon/NuGet/NuGetDownloadTool.cs` — call `GetPackageHealthAsync` after download; format health section in output; update tool `[Description]` to mention health data
  - `tests/SharpRecon.Tests/NuGet/NuGetDownloadToolTests.cs` — new test file for tool output formatting (healthy, deprecated, vulnerable, mixed scenarios)
  - `README.md` — update `nuget_download` tool description
  - `CHANGELOG.md` — add entry under `[Unreleased]`
- **Modified spec**: `openspec/specs/nuget-operations/spec.md` — add requirements and scenarios for deprecation, vulnerability, and metadata display
- **No new dependencies**: all data comes from `NuGet.Protocol` 7.3.0 types already referenced (`IPackageSearchMetadata`, `PackageDeprecationMetadata`, `PackageVulnerabilityMetadata`)
- **No new tools**: enriches existing output, keeping tool count stable and avoiding extra round-trips
- **No breaking changes**: new fields are appended to existing output
