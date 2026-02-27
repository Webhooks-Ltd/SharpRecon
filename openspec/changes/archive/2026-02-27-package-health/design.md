## Context

The `nuget_download` tool currently returns package ID, resolved version, cache path, and available TFMs. During wildcard/latest resolution, `NuGetService.ResolveVersionAsync` fetches all `IPackageSearchMetadata` objects via `PackageMetadataResource.GetMetadataAsync(string, ...)` but discards everything except the version number. For exact-version downloads, it skips metadata entirely. This means agents get no signal about deprecation, vulnerabilities, or age -- they can confidently recommend a package that NuGet.org would flag with warnings.

The NuGet SDK types we need are already available via `NuGet.Protocol` (no new dependency):
- `IPackageSearchMetadata.Published` -- `DateTimeOffset?`
- `IPackageSearchMetadata.Vulnerabilities` -- `IEnumerable<PackageVulnerabilityMetadata>` (sync property, each has `int Severity` and `Uri AdvisoryUrl`)
- `IPackageSearchMetadata.GetDeprecationMetadataAsync()` -- returns `PackageDeprecationMetadata` with `IEnumerable<string> Reasons`, `string Message`, and `AlternatePackageMetadata AlternatePackage` (which has `string PackageId` and `VersionRange Range`)

## Goals / Non-Goals

**Goals:**
- Surface deprecation, vulnerabilities, and publish date in every `nuget_download` response
- Use own domain types so the tool layer never touches NuGet SDK types
- Make health best-effort: metadata failure must not block download
- Always emit a health section (explicit "no issues" for healthy packages) so agents see a positive safety signal

**Non-Goals:**
- New tool or new MCP endpoint (enriches existing output)
- Prefix-reserved status (cut -- `PrefixReserved` not reliably populated by `PackageMetadataResource`)
- Dependency group listing (cut -- too noisy for tool output)
- Caching health metadata across calls (stale safety data is worse than a 200ms query)

## Decisions

### 1. Separate `GetPackageHealthAsync` method on `INuGetService`, not embedded in `DownloadPackageAsync`

**Choice:** Add `Task<PackageHealthInfo> GetPackageHealthAsync(string packageId, NuGetVersion resolvedVersion, CancellationToken ct)` to the `INuGetService` interface. The tool layer calls it after `DownloadPackageAsync`. The method throws on failure; the tool catches and handles the error.

**Why:** Health is conceptually separate from download. Embedding it in `DownloadPackageAsync` would mean the download method creates its own `SourceRepository`/`SourceCacheContext` and then the health query needs a second one anyway (or we'd have to thread shared infrastructure through). Keeping them separate means each method owns its NuGet SDK resources cleanly. The tool layer is the natural orchestrator -- it already has the try-catch boundary for error formatting.

**Alternative considered:** Return health inside `NuGetDownloadResult`. Rejected because it would force `DownloadPackageAsync` to swallow health failures internally (hiding error handling in the service) and couples the download result record to health concerns. The tool should decide what to do when health fails, not the service.

### 2. Always use the `PackageIdentity` overload for health, never reuse wildcard metadata

**Choice:** `GetPackageHealthAsync` always calls `PackageMetadataResource.GetMetadataAsync(PackageIdentity, SourceCacheContext, ILogger, CancellationToken)` to get the single `IPackageSearchMetadata` for the resolved version. It does not accept or reuse metadata from `ResolveVersionAsync`.

**Why:** Reusing metadata from the wildcard path saves one HTTP call (~200ms) but adds significant coupling. `ResolveVersionAsync` would need to return a tuple or stash the metadata somewhere, `DownloadPackageAsync` would need to thread it through, and the exact-version path still needs its own query anyway. Two code paths for the same data means two places for bugs. The `PackageIdentity` overload is a single targeted call that works identically for all resolution paths. The simplicity is worth 200ms.

**Alternative considered:** Change `ResolveVersionAsync` to return `(NuGetVersion, IPackageSearchMetadata?)` and pass the metadata through to avoid the second call on wildcard/latest paths. Rejected for the coupling reasons above. Also, `ResolveVersionAsync` is currently `static` -- threading an optional metadata object through would bloat its return type and every caller.

### 3. `ResolveVersionAsync` stays unchanged; no metadata call on the exact-version early-return path

**Choice:** `ResolveVersionAsync` continues to return immediately for exact versions (`NuGetVersion.TryParse` succeeds -> return). The health metadata query lives entirely in `GetPackageHealthAsync`, called separately by the tool.

**Why:** `ResolveVersionAsync` has a single responsibility: turn a version specifier into a concrete `NuGetVersion`. Adding metadata concerns to it would violate that. The tool orchestrates both calls in sequence -- first download (which may or may not call `ResolveVersionAsync`), then health.

### 4. `PackageHealthInfo` as a top-level record in `INuGetService.cs`

**Choice:** Define `PackageHealthInfo` and its nested types as top-level records in `INuGetService.cs`, alongside `NuGetDownloadResult` and `NuGetSearchResult`. Structure:

```csharp
internal record PackageHealthInfo(
    DateTimeOffset? Published,
    DeprecationInfo? Deprecation,
    IReadOnlyList<VulnerabilityInfo> Vulnerabilities);

internal record DeprecationInfo(
    IReadOnlyList<string> Reasons,
    string? Message,
    AlternatePackageInfo? AlternatePackage);

internal record AlternatePackageInfo(
    string PackageId,
    string VersionRange);

internal record VulnerabilityInfo(
    string Severity,
    Uri AdvisoryUrl);
```

**Why:** These are value-carrying records with no behavior -- same pattern as `NuGetDownloadResult`. Placing them in `INuGetService.cs` keeps all NuGet service contracts and their data shapes in one file. `VulnerabilityInfo.Severity` is `string` (already mapped from int: "Low", "Moderate", "High", "Critical", "Unknown(N)") so the tool layer does zero mapping -- it just renders what the service gives it. `AlternatePackageInfo.VersionRange` is `string` (already rendered via `VersionRange.ToString()` in the service) for the same reason.

**Alternative considered:** Nested records inside `PackageHealthInfo`. Rejected because `DeprecationInfo`, `VulnerabilityInfo`, and `AlternatePackageInfo` are independently useful types and nesting adds verbosity to every usage site.

### 5. `NuGetDownloadResult` does NOT carry health; tool receives health as a separate value

**Choice:** The tool calls `DownloadPackageAsync` and `GetPackageHealthAsync` independently and formats the combined output.

```csharp
var result = await nuGetService.DownloadPackageAsync(packageId, version, ct);

PackageHealthInfo? health = null;
try
{
    health = await nuGetService.GetPackageHealthAsync(
        packageId, NuGetVersion.Parse(result.ResolvedVersion), ct);
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    // metadata failure does not affect download result
}
```

The tool then formats the combined output. When `health` is non-null, the tool emits the full health section. When `health` is null (metadata failure), the tool emits the fallback message.

**Why:** This keeps the download result type stable (no breaking change to existing record), makes the error isolation explicit and visible in the tool (not hidden inside the service), and lets the tool format the health section or the fallback message based on whether `health` is null.

### 6. Error isolation via filtered catch in the tool layer

**Choice:** The tool wraps the `GetPackageHealthAsync` call in `catch (Exception ex) when (ex is not OperationCanceledException)`. If a non-cancellation exception is thrown, `health` stays null and the tool emits `"Health: unavailable (metadata query failed)"` in the output. `OperationCanceledException` propagates normally so MCP request cancellation is not suppressed.

**Why:** The service method should throw on failure (not return null for "I tried and failed"). The tool is the error boundary. This avoids the service having to distinguish "no data available" from "query failed" in its return type. The cancellation filter is critical: a bare `catch` would silently eat client cancellation, making the tool appear to succeed when the request was actually cancelled.

**Alternative considered:** `GetPackageHealthAsync` returns `null` on failure (internal try-catch). Rejected because it hides errors and makes it impossible for callers to distinguish "package has no metadata" from "network failed."

### 7. Severity mapping lives in `GetPackageHealthAsync`, not in the tool

**Choice:** The service maps `int Severity` to a string label when constructing `VulnerabilityInfo`. The tool renders the string as-is.

```csharp
static string MapSeverity(int severity) => severity switch
{
    0 => "Low",
    1 => "Moderate",
    2 => "High",
    3 => "Critical",
    _ => $"Unknown({severity})"
};
```

**Why:** Severity mapping is a translation of NuGet SDK types into domain types -- that's the service's job. The tool should only format domain types into text. Same reasoning applies to `VersionRange.ToString()` for the alternate package: the service renders it to a string, the tool just includes it.

### 8. Updated tool description

**Choice:** New `[Description]` text:

```
Downloads a NuGet package to the local cache. Call this first — all other tools require the exact version from this response. If you don't know the exact package ID, call nuget_search first. Returns resolved version, cache path, available TFMs, and package health (deprecation, vulnerabilities, publish date).
```

**Why:** Gateway instruction ("call this first") is positioned early for routing priority. Health data is mentioned last as a return-value detail so agents know to look for it. Does not enumerate every health field (that would bloat the tool description).

### 9. Health section output format

**Choice:** The tool always emits a `Health:` block in the output with a consistent structure. Three states:

**Healthy package:**
```
Health:
  Published: 2023-03-08
  No deprecation notices. No known vulnerabilities.
```

**Unhealthy package (deprecated + vulnerable):**
```
Health:
  Published: 2018-11-27
  DEPRECATED (Legacy): "Use Azure.Storage.Blobs instead"
    Alternate: Azure.Storage.Blobs [>= 12.0.0]
  Vulnerabilities:
    - HIGH: https://github.com/advisories/GHSA-xxx
    - MODERATE: https://github.com/advisories/GHSA-yyy
```

**Metadata unavailable:**
```
Health: unavailable (metadata query failed)
```

**Why:** Structured key-value format is significantly more reliable for LLM parsing than prose paragraphs. The `Health:` prefix acts as a structural marker the LLM can locate in context. `DEPRECATED` in uppercase is a visual signal that LLMs attend to reliably. The healthy-package case uses explicit "No deprecation notices. No known vulnerabilities." so the LLM sees a positive safety signal rather than ambiguous absence. When `Published` is null, render "Published: unknown".

## Risks / Trade-offs

- **[Extra HTTP call on exact-version path]** Every exact-version download now makes a metadata query that didn't exist before (~200ms). This is acceptable: the call is fast, the safety information is valuable, and it keeps the code paths uniform. There is no option to skip it -- the spec requires health for all downloads.

- **[Two `SourceRepository` + `SourceCacheContext` instances per download]** `DownloadPackageAsync` and `GetPackageHealthAsync` each create their own. The NuGet SDK `Repository.Factory.GetCoreV3` returns lightweight objects and `SourceCacheContext` is cheap. The alternative (sharing them) would require threading SDK types through the interface boundary, coupling the two methods.

- **[Filtered catch in the tool]** Swallowing non-cancellation exceptions from `GetPackageHealthAsync` risks hiding bugs during development. Mitigation: the tool emits the fallback message, which is visible in the MCP response. `OperationCanceledException` is explicitly excluded from the catch so MCP request cancellation propagates correctly.

- **[`GetDeprecationMetadataAsync` is async]** Building `PackageHealthInfo` requires awaiting `GetDeprecationMetadataAsync()` after getting the `IPackageSearchMetadata`. This is a second async hop inside `GetPackageHealthAsync`. No way around it -- the NuGet SDK models it as a lazy load.

- **[Null `Published` from NuGet API]** `IPackageSearchMetadata.Published` is `DateTimeOffset?`. For unlisted or edge-case packages it may be null. The record carries it as nullable; the tool renders "Published: unknown" when null.
