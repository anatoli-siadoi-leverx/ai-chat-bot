using GoogleChatBot.Models.Incoming;
using GoogleChatBot.Models.Outgoing;

namespace GoogleChatBot.Commands;

/// <summary>
/// Routes incoming messages to the matching <see cref="ICommand"/>.
/// </summary>
public sealed class CommandDispatcher
{
    private readonly IReadOnlyList<ICommand> _commands;
    private readonly ILogger<CommandDispatcher> _logger;

    public CommandDispatcher(
        IEnumerable<ICommand> commands,
        ILogger<CommandDispatcher> logger)
    {
        _commands = commands.ToList();
        _logger = logger;
    }

    /// <summary>
    /// Tries to dispatch the message to a registered command.
    /// Returns <c>null</c> when the message text does not start with <c>/</c>.
    /// </summary>
    public async Task<BotResponse?> DispatchAsync(ChatMessage message)
    {
        var input = message.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(input) || input[0] != '/')
        {
            return null;
        }

        var command = _commands.FirstOrDefault(c => c.CanHandle(input));

        if (command is null)
        {
            var name = input.Split(' ')[0];
            _logger.LogWarning("Unknown command: {Command}", name);

            return BotResponse.FromText($"Unknown command: `{name}`. Type `/help` to see available commands.");
        }

        _logger.LogInformation("Dispatching command /{Command}", command.Name);

        return await command.ExecuteAsync(message);
    }
}
