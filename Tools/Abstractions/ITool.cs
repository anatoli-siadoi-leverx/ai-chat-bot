namespace Tools.Abstractions;

/// <summary>
/// A single executable tool that encapsulates a piece of business logic.
/// Tools are reused across Commands (UI layer), MCP Server, and LLM function calling.
/// </summary>
public interface ITool
{
    /// <summary>Unique tool name used for routing and OpenAI function name, e.g. "hello".</summary>
    string Name { get; }

    /// <summary>One-line description shown in /help and passed to the LLM as function description.</summary>
    string Description { get; }

    /// <summary>
    /// JSON Schema (as a string) describing the tool's input parameters.
    /// Passed to OpenAI as the function's <c>parameters</c> field.
    /// Use <c>{"type":"object","properties":{}}</c> for tools that take no arguments.
    /// </summary>
    string InputSchema { get; }

    /// <summary>
    /// Executes the tool with the given <paramref name="input"/> string.
    /// For LLM calls the input is the first extracted string argument from the JSON payload.
    /// For Command calls the input is the raw argument after the command prefix.
    /// </summary>
    Task<string> ExecuteAsync(string input);
}
