using Domain.Repositories;
using GoogleChatBot.Models.Incoming;
using GoogleChatBot.Models.Outgoing;

namespace GoogleChatBot.Commands;

/// <summary>
/// /clean — permanently deletes all ticket history from the database.
/// </summary>
public sealed class CleanCommand(ITicketRepository repo) : ICommand
{
    public string Name => "clean";
    public string Description => "Deletes all ticket history from the database.";

    public bool CanHandle(string input)
        => input.Equals("/clean", StringComparison.OrdinalIgnoreCase);

    public async Task<BotResponse> ExecuteAsync(ChatMessage message)
    {
        var all   = await repo.GetAllAsync();

        var count = all.Count;

        await repo.ClearAllAsync();

        return BotResponse.FromText(
            count == 0
                ? "🗑️ Nothing to clean — ticket history is already empty."
                : $"🗑️ Deleted *{count}* ticket(s). History is now empty.");
    }
}
