# SDK Validation Notes (ModelContextProtocol 1.0.0)

## Tool Registration
- `[McpServerToolType]` on class, `[McpServerTool(Name = "tool_name")]` on static method
- `[Description("...")]` from `System.ComponentModel` for tool and parameter descriptions
- `WithToolsFromAssembly()` scans the calling assembly for annotated types
- DI services are injected as method parameters alongside tool parameters

## Transport
- Stdio transport uses newline-delimited JSON-RPC (not Content-Length framing)
- Logging MUST go to stderr (`LogToStandardErrorThreshold = LogLevel.Trace`)

## Tool Results
- Returning `string` auto-wraps into `{"content":[{"type":"text","text":"..."}]}`
- `isError` field in result indicates error vs success

## Error Handling
- Thrown exceptions are caught by the SDK framework
- Returns `{"content":[{"type":"text","text":"An error occurred invoking 'tool_name'."}],"isError":true}`
- The generic message hides the actual exception — for actionable messages, catch exceptions and return error text manually
- The connection survives exceptions — they don't corrupt the stdio stream

## Explicit Error Results
- Tools can return `CallToolResult` (from `ModelContextProtocol.Protocol`) directly
- Set `IsError = true` and `Content = [new TextContentBlock { Text = "..." }]` for custom error messages
- This gives full control over error text — the SDK's generic message ("An error occurred invoking...") is not helpful
- Both `string` and `CallToolResult` are valid return types for `[McpServerTool]` methods

## Pattern for SharpRecon Tools
```csharp
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerTool(Name = "tool_name"), Description("...")]
public static CallToolResult MyTool(string param)
{
    try
    {
        var result = DoWork(param);
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
}
```
