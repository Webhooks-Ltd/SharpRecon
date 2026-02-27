---
name: compare-versions
description: Compares the public API surface of a NuGet package between two versions to identify breaking changes and new APIs.
argument-hint: <packageId> <fromVersion> <toVersion>
---

Compare the public API surface of $0 between v$1 and v$2.

Steps:
1. Call `nuget_download` for $0 v$1, then again for v$2. Save both exact versions.
2. Call `assembly_list` for both versions to identify the main assembly (usually named after the package).
3. Call `type_list` for both versions on the main assembly.
4. Diff the type lists:
   - Types added in v$2
   - Types removed in v$2
   - Types present in both versions
5. For types present in both, call `type_detail` on each version and compare member signatures to find:
   - Members added
   - Members removed (breaking)
   - Signature changes (breaking)
6. Summarize findings as:
   - **Breaking changes**: removed types, removed members, changed signatures
   - **New APIs**: added types, added members
   - **Unchanged**: types/members with identical signatures
