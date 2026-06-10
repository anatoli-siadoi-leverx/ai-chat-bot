namespace GoogleChatBot.Commands;

/// <summary>
/// Routes incoming message text to the matching <see cref="ICommand"/>.
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
        _logger   = logger;
    }

    /// <summary>
    /// Tries to dispatch <paramref name="input"/> to a registered command.
    /// </summary>
    /// <returns>
    /// The command's response text, an "unknown command" message,
    /// or <c>null</c> when the input does not start with <c>/</c>.
    /// </returns>
    public async Task<string?> DispatchAsync(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || input[0] != '/')
            return null;

        var command = _commands.FirstOrDefault(c => c.CanHandle(input));

        if (command is null)
        {
            var name = input.Split(' ')[0];
            _logger.LogWarning("Unknown command: {Command}", name);
            return $"Unknown command: `{name}`. Type `/help` to see available commands.";
        }

        _logger.LogInformation("Dispatching command /{Command}", command.Name);
        return await command.ExecuteAsync(input);
    }
}
