using System.ComponentModel;
using ModelContextProtocol.Server;

namespace SharpRecon.Prompts;

[McpServerPromptType]
internal sealed class CompareVersionsPrompt
{
    [McpServerPrompt(Name = "compare_versions")]
    [Description("Compares the public API surface of a NuGet package between two versions to identify breaking changes and new APIs.")]
    public static string Compare(
        [Description("NuGet package ID, e.g. 'Newtonsoft.Json'")] string packageId,
        [Description("Older version, e.g. '12.0.3'")] string fromVersion,
        [Description("Newer version, e.g. '13.0.3'")] string toVersion)
    {
        return $"""
            Compare the public API surface of {packageId} between v{fromVersion} and v{toVersion}.

            Steps:
            1. Call `nuget_download` for {packageId} v{fromVersion}, then again for v{toVersion}. Save both exact versions.
            2. Call `assembly_list` for both versions to identify the main assembly (usually named after the package).
            3. Call `type_list` for both versions on the main assembly.
            4. Diff the type lists:
               - Types added in v{toVersion}
               - Types removed in v{toVersion}
               - Types present in both versions
            5. For types present in both, call `type_detail` on each version and compare member signatures to find:
               - Members added
               - Members removed (breaking)
               - Signature changes (breaking)
            6. Summarize findings as:
               - **Breaking changes**: removed types, removed members, changed signatures
               - **New APIs**: added types, added members
               - **Unchanged**: types/members with identical signatures
            """;
    }
}
