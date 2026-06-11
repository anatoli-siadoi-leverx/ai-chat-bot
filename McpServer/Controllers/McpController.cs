using McpServer.Models;
using Microsoft.AspNetCore.Mvc;
using Tools;

namespace McpServer.Controllers;

[ApiController]
[Route("mcp")]
[Produces("application/json")]
public sealed class McpController : ControllerBase
{
    private readonly ToolRegistry _registry;

    public McpController(ToolRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>Returns metadata for all registered tools.</summary>
    // GET /mcp/tools
    [HttpGet("tools")]
    [ProducesResponseType<ToolListResponse>(StatusCodes.Status200OK)]
    public ActionResult<ToolListResponse> ListTools()
    {
        var tools = _registry.GetAll()
            .Select(t => new ToolInfo(t.Name, t.Description, t.InputSchema))
            .ToList();

        return Ok(new ToolListResponse(tools));
    }

    /// <summary>Calls a tool by name and returns its result.</summary>
    // POST /mcp/tools/call
    [HttpPost("tools/call")]
    [ProducesResponseType<ToolCallResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ToolCallResponse>> CallTool([FromBody] ToolCallRequest request)
    {
        var tool = _registry.Find(request.ToolName);

        if (tool is null)
            return NotFound(new { Error = $"Tool '{request.ToolName}' not found." });

        var result = await tool.ExecuteAsync(request.Input);

        return Ok(new ToolCallResponse(result));
    }
}
