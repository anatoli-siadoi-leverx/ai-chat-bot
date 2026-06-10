using McpServer.Models;
using Microsoft.AspNetCore.Mvc;
using Tools;

namespace McpServer.Controllers;

[ApiController]
[Route("mcp")]
public sealed class McpController : ControllerBase
{
    private readonly ToolRegistry _registry;

    public McpController(ToolRegistry registry)
    {
        _registry = registry;
    }

    // GET /mcp/tools
    // Returns the list of all registered tools with their metadata.
    [HttpGet("tools")]
    public ActionResult<ToolListResponse> ListTools()
    {
        var tools = _registry.GetAll()
            .Select(t => new ToolInfo(t.Name, t.Description, t.InputSchema))
            .ToList();

        return Ok(new ToolListResponse(tools));
    }

    // POST /mcp/tools/call
    // Body: { "toolName": "hello", "input": "Alice" }
    // Calls the named tool and returns its result.
    [HttpPost("tools/call")]
    public async Task<ActionResult<ToolCallResponse>> CallTool([FromBody] ToolCallRequest request)
    {
        var tool = _registry.Find(request.ToolName);

        if (tool is null)
            return NotFound(new { Error = $"Tool '{request.ToolName}' not found." });

        var result = await tool.ExecuteAsync(request.Input);

        return Ok(new ToolCallResponse(result));
    }
}
