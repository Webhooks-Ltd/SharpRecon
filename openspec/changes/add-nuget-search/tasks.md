## 1. Service Layer

- [x] 1.1 Add `SearchAsync` method to `INuGetService` interface
- [x] 1.2 Implement `SearchAsync` in `NuGetService` using HttpClient to call NuGet V3 Search API
- [x] 1.3 Create `NuGetSearchResult` model (PackageId, Version, Description, TotalDownloads, Verified)
- [x] 1.4 Add download count formatting helper (e.g. 12345678 → "12.3M")

## 2. MCP Tool

- [x] 2.1 Create `NuGetSearchTool.cs` with `nuget_search` tool method (query, take parameters)
- [x] 2.2 Format results as markdown (numbered list with packageId, version, verified badge, downloads, description)
- [x] 2.3 Add input validation (empty query, take capping at 20)
- [x] 2.4 Add error handling (network failures, empty results)

## 3. DI Registration

- [x] 3.1 Register named HttpClient for NuGet search via IHttpClientFactory

## 4. Tests

- [x] 4.1 Unit test for download count formatting
- [x] 4.2 Unit test for search result markdown formatting
- [x] 4.3 Integration test for NuGetSearchTool with mocked service

## 5. Documentation

- [x] 5.1 Update README.md tools table with `nuget_search`
- [x] 5.2 Update CHANGELOG.md
