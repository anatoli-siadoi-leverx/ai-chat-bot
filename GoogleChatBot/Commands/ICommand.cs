using GoogleChatBot.Models.Incoming;
using GoogleChatBot.Models.Outgoing;

namespace GoogleChatBot.Commands;

/// <summary>
/// A single slash-command handler.
/// </summary>
public interface ICommand
{
    /// <summary>Command name without the leading slash, e.g. "help".</summary>
    string Name { get; }

    /// <summary>One-line description shown in /help output.</summary>
    string Description { get; }

    /// <summary>Returns true when this command can handle the given input text.</summary>
    bool CanHandle(string input);

    /// <summary>
    /// Executes the command. The full <see cref="ChatMessage"/> is provided so commands
    /// can inspect both the text (<c>message.Text</c>) and any file attachments
    /// (<c>message.Attachments</c>).
    /// </summary>
    Task<BotResponse> ExecuteAsync(ChatMessage message);
}
