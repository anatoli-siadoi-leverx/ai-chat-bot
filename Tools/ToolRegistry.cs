using Tools.Abstractions;

namespace Tools;

/// <summary>
/// Holds all registered <see cref="ITool"/> instances.
/// Injected into the MCP controller and (later) the LLM agent loop.
/// </summary>
public sealed class ToolRegistry
{
    private readonly IReadOnlyList<ITool> _tools;

    public ToolRegistry(IEnumerable<ITool> tools)
    {
        _tools = tools.ToList();
    }

    /// <summary>Returns all registered tools.</summary>
    public IReadOnlyList<ITool> GetAll() => _tools;

    /// <summary>Finds a tool by name (case-insensitive). Returns null if not found.</summary>
    public ITool? Find(string name) =>
        _tools.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}
