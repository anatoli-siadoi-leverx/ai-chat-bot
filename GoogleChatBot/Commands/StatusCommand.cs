using Domain.Repositories;
using GoogleChatBot.Models.Outgoing;

namespace GoogleChatBot.Commands;

/// <summary>
/// /status — lists all error tickets with their current states.
/// </summary>
public sealed class StatusCommand : ICommand
{
    private readonly ITicketRepository _repo;

    public string Name => "status";
    public string Description => "Lists all error tickets and their states.";

    public StatusCommand(ITicketRepository repo) => _repo = repo;

    public bool CanHandle(string input)
        => input.Equals("/status", StringComparison.OrdinalIgnoreCase) ||
           input.StartsWith("/status ", StringComparison.OrdinalIgnoreCase);

    public async Task<BotResponse> ExecuteAsync(string input)
    {
        var tickets = await _repo.GetAllAsync();

        if (tickets.Count == 0)
            return BotResponse.FromText("No tickets found. Use `/ticket <description>` to create one.");

        var lines = tickets.Select(t =>
            $"• `{t.Id:D}` — **{t.Title}** | State: `{t.State}` | {t.CreatedAt:yyyy-MM-dd HH:mm} UTC");

        return BotResponse.FromText(
            $"*Error Tickets ({tickets.Count} total):*\n" + string.Join("\n", lines));
    }
}
