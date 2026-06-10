using Tools;

namespace GoogleChatBot.Commands;

/// <summary>
/// /hello [name] — delegates greeting logic to <see cref="HelloTool"/>.
/// Parses the name from input; tool owns the actual message format.
/// </summary>
public sealed class HelloCommand : ICommand
{
    private readonly HelloTool _tool;

    public string Name        => "hello";
    public string Description => "Greets you by name — usage: `/hello [name]`";

    public HelloCommand(HelloTool tool)
    {
        _tool = tool;
    }

    public bool CanHandle(string input) =>
        input.Equals("/hello", StringComparison.OrdinalIgnoreCase) ||
        input.StartsWith("/hello ", StringComparison.OrdinalIgnoreCase);

    public Task<string> ExecuteAsync(string input)
    {
        // Split "/hello John Doe" → ["hello", "John Doe"], pass "John Doe" to tool.
        var parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var name  = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        return _tool.ExecuteAsync(name);
    }
}
