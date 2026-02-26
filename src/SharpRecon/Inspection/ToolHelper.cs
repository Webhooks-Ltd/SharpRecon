using ModelContextProtocol.Protocol;

namespace SharpRecon.Inspection;

internal static class ToolHelper
{
    internal static readonly SemaphoreSlim HeavyOperationSemaphore = new(1, 1);

    internal static async Task<CallToolResult> ExecuteWithSemaphoreAsync(Func<Task<string>> operation, CancellationToken ct)
    {
        await HeavyOperationSemaphore.WaitAsync(ct);
        try
        {
            var result = await operation();
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = result }],
            };
        }
        catch (Exception ex)
        {
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = ex.Message }],
                IsError = true,
            };
        }
        finally
        {
            HeavyOperationSemaphore.Release();
        }
    }

    internal static string? ValidateExactVersion(string version)
    {
        if (version.Contains('*'))
            return "Wildcard versions are not supported for this tool. Use nuget_download first to resolve the exact version.";
        return null;
    }

    internal static string? ValidateParameterTypes(string[]? parameterTypes)
    {
        if (parameterTypes is null) return null;

        string[] csharpAliases = ["string", "int", "long", "bool", "double", "float", "decimal", "char", "byte", "sbyte", "short", "ushort", "uint", "ulong", "object", "void", "nint", "nuint"];

        foreach (var pt in parameterTypes)
        {
            if (Array.IndexOf(csharpAliases, pt) >= 0)
                return $"Use fully qualified CLR type names in parameterTypes (e.g. 'System.String', not 'string'). Got '{pt}'.";
        }
        return null;
    }
}
