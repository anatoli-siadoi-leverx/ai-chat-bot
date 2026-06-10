namespace McpServer.Models;

/// <summary>Metadata about a single tool exposed by the registry.</summary>
public record ToolInfo(
    string Name,
    string Description,
    string InputSchema);

/// <summary>Response body for GET /mcp/tools.</summary>
public record ToolListResponse(IReadOnlyList<ToolInfo> Tools);

/// <summary>Request body for POST /mcp/tools/call.</summary>
public record ToolCallRequest(string ToolName, string Input);

/// <summary>Success response for POST /mcp/tools/call.</summary>
public record ToolCallResponse(string Result);
