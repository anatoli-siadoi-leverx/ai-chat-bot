using Tools.Abstractions;

namespace Tools;

/// <summary>
/// Returns a personalised greeting.
/// Input: the name to greet (empty → "World").
/// </summary>
public sealed class HelloTool : ITool
{
    public string Name        => "hello";
    public string Description => "Greets a person by name";

    public Task<string> ExecuteAsync(string input)
    {
        var name = string.IsNullOrWhiteSpace(input) ? "World" : input.Trim();
        return Task.FromResult($"Hello, *{name}*!");
    }
}
