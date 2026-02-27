---
name: investigate-package
description: Guides step-by-step investigation of a NuGet package — download, browse types, read signatures, and optionally decompile source.
argument-hint: <package> [goal]
---

Investigate the NuGet package: $0

$1

Follow this workflow, stopping as soon as the goal is satisfied:

1. If the package argument looks like a search query rather than an exact package ID, call `nuget_search` to find the right package ID.
2. Call `nuget_download` with the package ID. Save the exact version from the response — all subsequent tools require it.
3. Call `assembly_list` to see what assemblies and TFMs the package contains.
4. To find types by name, call `type_search` (does not require assembly name). To browse all types in an assembly, call `type_list` (requires assembly name from step 3).
5. Call `type_detail` on interesting types to see their full API surface with XML docs. This is fast — no decompilation.
6. Call `member_detail` to drill into specific members and see all overloads with parameter documentation.
7. Only call `decompile_type` or `decompile_member` when you need to see implementation source code.

Key rules:
- Always pass the exact version string from step 2 to all subsequent tools.
- Type names must be fully qualified (e.g. 'Newtonsoft.Json.JsonConvert', not 'JsonConvert').
- Assembly names omit the .dll extension.
- For parameterTypes, use CLR names not C# aliases: string->System.String, int->System.Int32, bool->System.Boolean, object->System.Object.
- Prefer type_detail/member_detail over decompile tools unless source code is specifically needed.
