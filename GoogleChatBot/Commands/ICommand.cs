namespace GoogleChatBot.Commands;

/// <summary>
/// A single slash-command handler. Commands are the UI layer:
/// they parse user input and delegate business logic to <c>Tools</c>.
/// </summary>
public interface ICommand
{
    /// <summary>Command name without the leading slash, e.g. "help".</summary>
    string Name { get; }

    /// <summary>One-line description shown in /help output.</summary>
    string Description { get; }

    /// <summary>
    /// Returns true when this command can handle the given input.
    /// <paramref name="input"/> is already trimmed.
    /// </summary>
    bool CanHandle(string input);

    /// <summary>
    /// Executes the command and returns the text to send back to the user.
    /// Async because commands delegate to Tools, which may perform I/O.
    /// </summary>
    Task<string> ExecuteAsync(string input);
}
