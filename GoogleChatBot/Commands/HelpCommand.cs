using GoogleChatBot.Models.Incoming;
using GoogleChatBot.Models.Outgoing;

namespace GoogleChatBot.Commands;

/// <summary>
/// /help — lists all available commands by reading their Name and Description.
/// </summary>
public sealed class HelpCommand : ICommand
{
    private readonly IReadOnlyList<ICommand> _commands;

    public string Name => "help";
    public string Description => "Shows all available commands";

    public HelpCommand(IReadOnlyList<ICommand> commands)
    {
        _commands = commands;
    }

    public bool CanHandle(string input) =>
        input.Equals("/help", StringComparison.OrdinalIgnoreCase) ||
        input.StartsWith("/help ", StringComparison.OrdinalIgnoreCase);

    public Task<BotResponse> ExecuteAsync(ChatMessage message)
    {
        // Prepend(this) so /help also lists itself in the output.
        var lines = _commands
            .Prepend(this)
            .OrderBy(c => c.Name)
            .Select(c => $"• `/{c.Name}` — {c.Description}");

        return Task.FromResult(BotResponse.FromText("*Available commands:*\n" + string.Join("\n", lines)));
    }
}
