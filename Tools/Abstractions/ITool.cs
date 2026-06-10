namespace Tools.Abstractions;

/// <summary>
/// A single executable tool that encapsulates a piece of business logic.
/// Tools are reused across Commands (UI layer), MCP Server, and LLM function calling.
/// </summary>
public interface ITool
{
    /// <summary>Unique tool name used for routing, e.g. "hello", "time".</summary>
    string Name { get; }

    /// <summary>One-line description shown in /help and exposed via MCP tools/list.</summary>
    string Description { get; }

    /// <summary>
    /// Executes the tool with the given <paramref name="input"/> string.
    /// Input semantics are tool-specific (e.g. a name, a JSON payload, a file path).
    /// Returns the result as a plain string.
    /// </summary>
    Task<string> ExecuteAsync(string input);
}
