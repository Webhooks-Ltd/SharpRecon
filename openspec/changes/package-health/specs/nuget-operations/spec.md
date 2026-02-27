## MODIFIED Requirements

### Requirement: Download NuGet package
The `nuget_download` tool SHALL download a NuGet package to the standard global packages cache (`~/.nuget/packages` or `NUGET_PACKAGES` env var) using `NuGet.Packaging.PackageExtractor` or equivalent to ensure cache entries are identical to `dotnet restore` output (including `.nupkg.metadata` and `.nupkg.sha512` files). The tool SHALL accept an optional version parameter supporting exact versions, wildcard patterns, or omission for latest stable. The tool SHALL only download from nuget.org (hardcoded source). The response SHALL include package health metadata (deprecation, vulnerabilities, publish date) alongside the package ID, resolved version, cache path, and available TFMs.

#### Scenario: Download with exact version
- **WHEN** agent calls `nuget_download` with packageId `Newtonsoft.Json` and version `13.0.3`
- **THEN** the package is downloaded to the global cache and the response contains the package ID, resolved version `13.0.3`, cache path, list of available TFMs, and a health section with deprecation status, vulnerability advisories, and publish date

#### Scenario: Download with wildcard version
- **WHEN** agent calls `nuget_download` with packageId `Newtonsoft.Json` and version `13.*`
- **THEN** the system queries NuGet for the highest stable version matching `>= 13.0.0, < 14.0.0`, downloads it, and returns the resolved version and health metadata

#### Scenario: Download with minor wildcard
- **WHEN** agent calls `nuget_download` with packageId `Newtonsoft.Json` and version `13.0.*`
- **THEN** the system queries NuGet for the highest stable version matching `>= 13.0.0, < 13.1.0`, downloads it, and returns the resolved version and health metadata

#### Scenario: Download latest stable
- **WHEN** agent calls `nuget_download` with packageId `Newtonsoft.Json` and no version
- **THEN** the system queries NuGet for the latest stable version, downloads it, and returns the resolved version and health metadata

#### Scenario: Wildcard resolution is always fresh
- **WHEN** agent calls `nuget_download` with version `2.*` multiple times
- **THEN** each call re-queries NuGet for the highest matching version (no caching of wildcard resolution)

#### Scenario: Package not found
- **WHEN** agent calls `nuget_download` with a packageId that does not exist on nuget.org
- **THEN** the tool returns a structured error message: "Package '{packageId}' not found on nuget.org"

#### Scenario: Version not found
- **WHEN** agent calls `nuget_download` with a version or wildcard that matches no published versions
- **THEN** the tool returns a structured error message: "No version matching '{version}' found for package '{packageId}'"

#### Scenario: Package already cached
- **WHEN** agent calls `nuget_download` for a package and exact version already present in the global cache
- **THEN** the tool returns success immediately without re-downloading, but still queries NuGet for health metadata and includes it in the response

## ADDED Requirements

### Requirement: Package health metadata
The `nuget_download` tool SHALL query `PackageMetadataResource` using the `PackageIdentity` overload for the resolved version and return a health section in every successful response. This is always a dedicated call, separate from version resolution. The health section SHALL use own domain types (`PackageHealthInfo` record), not raw NuGet SDK types. The metadata query SHALL be performed for all resolution paths (exact version, wildcard, latest stable, and cached).

The health section SHALL always be present in successful responses with a consistent structure:
- **Deprecation**: "none" when not deprecated, or deprecation details when deprecated
- **Vulnerabilities**: "none" when no known vulnerabilities, or a list of advisories
- **Published**: the publish date of the resolved version

Vulnerability severity SHALL be mapped from the integer `Severity` property:

| Int | Label |
|-----|-------|
| 0 | Low |
| 1 | Moderate |
| 2 | High |
| 3 | Critical |
| other | Unknown (N) |

Deprecation reasons are string values as returned by the NuGet API (e.g. `"Legacy"`, `"CriticalBugs"`) and SHALL be passed through without mapping. The alternate package version range, when present, SHALL be rendered using `VersionRange.ToString()` (NuGet range syntax, e.g. `[1.0.0, 2.0.0)`).

Building the health record requires two async operations: `GetMetadataAsync` for the `IPackageSearchMetadata`, then `GetDeprecationMetadataAsync()` on the result for deprecation details. `Vulnerabilities` is a synchronous property on `IPackageSearchMetadata`.

#### Scenario: Healthy package
- **WHEN** agent calls `nuget_download` for a package with no deprecation and no known vulnerabilities
- **THEN** the response includes a health section in this format:
  ```
  Published: 2023-03-08
  No deprecation notices. No known vulnerabilities.
  ```

#### Scenario: Deprecated package with alternate
- **WHEN** agent calls `nuget_download` for a package that has been deprecated on nuget.org with an alternate package specified
- **THEN** the response includes a health section in this format:
  ```
  Published: 2018-11-27
  DEPRECATED (Legacy): "Use Azure.Storage.Blobs instead"
    Alternate: Azure.Storage.Blobs [>= 12.0.0]
  No known vulnerabilities.
  ```

#### Scenario: Deprecated package without alternate
- **WHEN** agent calls `nuget_download` for a deprecated package where no alternate package is specified
- **THEN** the health section shows the deprecation reasons and message but omits the alternate line:
  ```
  Published: 2019-05-01
  DEPRECATED (CriticalBugs): "This package has critical security issues"
  No known vulnerabilities.
  ```

#### Scenario: Vulnerable package
- **WHEN** agent calls `nuget_download` for a package version with known vulnerabilities
- **THEN** the response includes a health section listing each vulnerability:
  ```
  Published: 2021-08-10
  No deprecation notices.
  Vulnerabilities:
    - HIGH: https://github.com/advisories/GHSA-xxx
    - MODERATE: https://github.com/advisories/GHSA-yyy
  ```

#### Scenario: Deprecated and vulnerable package
- **WHEN** agent calls `nuget_download` for a package that is both deprecated and has known vulnerabilities
- **THEN** the health section includes both deprecation details and vulnerability list

#### Scenario: Unknown vulnerability severity
- **WHEN** a vulnerability advisory has a severity integer outside the range 0-3
- **THEN** the severity is rendered as "Unknown (N)" where N is the raw integer value

#### Scenario: Metadata query failure
- **WHEN** the health metadata query fails (e.g. network error, API timeout)
- **THEN** the download still succeeds and returns the package ID, resolved version, cache path, and available TFMs. The health section is replaced with: "Health: unavailable (metadata query failed)". Subsequent tool calls (assembly_list, type_detail, etc.) work normally.

#### Scenario: Network failure during download
- **WHEN** a network error (timeout, connection refused, etc.) occurs during the package download itself
- **THEN** the tool returns a structured error message describing the failure without corrupting the MCP stdio connection
