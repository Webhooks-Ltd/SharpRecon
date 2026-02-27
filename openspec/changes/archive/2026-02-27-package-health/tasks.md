## 1. Domain Types

- [x] 1.1 Add `PackageHealthInfo`, `DeprecationInfo`, `AlternatePackageInfo`, and `VulnerabilityInfo` records to `src/SharpRecon/NuGet/INuGetService.cs` alongside the existing result records (Design Decision 4)
- [x] 1.2 Add `GetPackageHealthAsync(string packageId, NuGetVersion resolvedVersion, CancellationToken ct)` to the `INuGetService` interface (Design Decision 1)

## 2. Service Implementation

- [x] 2.1 Implement `GetPackageHealthAsync` in `NuGetService`: create own `SourceRepository` and `SourceCacheContext`, call `PackageMetadataResource.GetMetadataAsync(PackageIdentity, ...)`, await `GetDeprecationMetadataAsync()`, read synchronous `Vulnerabilities` property, and map results to domain records (Design Decisions 2, 3, 7)
- [x] 2.2 Add the `MapSeverity(int)` static method to `NuGetService` mapping 0→Low, 1→Moderate, 2→High, 3→Critical, other→`Unknown(N)` (Design Decision 7)

## 3. Tool Layer

- [x] 3.1 Update `NuGetDownloadTool.DownloadAsync`: after `DownloadPackageAsync` succeeds, call `GetPackageHealthAsync` in a filtered catch block (`when (ex is not OperationCanceledException)`) with `health` staying null on failure (Design Decisions 5, 6)
- [x] 3.2 Implement `FormatHealth(PackageHealthInfo? health, StringBuilder sb)` helper in the tool: emit the `Health:` block with `Published`, deprecation details (with optional `Alternate:` line), and vulnerabilities list; emit `"Health: unavailable (metadata query failed)"` when `health` is null; render `Published` as `"unknown"` when the `DateTimeOffset?` is null (Design Decision 9)
- [x] 3.3 Update the `[Description]` attribute on `DownloadAsync` to: `"Downloads a NuGet package to the local cache. Call this first — all other tools require the exact version from this response. If you don't know the exact package ID, call nuget_search first. Returns resolved version, cache path, available TFMs, and package health (deprecation, vulnerabilities, publish date)."` (Design Decision 8)

## 4. Tests

- [x] 4.1 Add `GetPackageHealthAsync` tests to `tests/SharpRecon.Tests/NuGet/NuGetServiceTests.cs`: verify severity mapping for all five cases (0–3 and out-of-range) using `NuGetService.MapSeverity` directly
- [x] 4.2 Create `tests/SharpRecon.Tests/NuGet/NuGetDownloadToolTests.cs` with a mocked `INuGetService`; cover: healthy package output (published date, "No deprecation notices. No known vulnerabilities."), deprecated package with alternate, deprecated package without alternate, vulnerable package, deprecated-and-vulnerable package, metadata query failure (health null → "Health: unavailable (metadata query failed)"), null `Published` renders as "unknown"

## 5. Docs

- [x] 5.1 Update `README.md`: revise the `nuget_download` tool description to mention the health section (deprecation, vulnerabilities, publish date)
- [x] 5.2 Add an entry to `CHANGELOG.md` under `[Unreleased]`: enriched `nuget_download` response with package health metadata (deprecation status, vulnerability advisories, publish date)
