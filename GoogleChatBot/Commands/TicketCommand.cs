using Domain.Repositories;
using Domain.Tickets;
using Domain.Workflow;
using GoogleChatBot.Cards;
using GoogleChatBot.Models.Outgoing;

namespace GoogleChatBot.Commands;

/// <summary>
/// /ticket &lt;description&gt; — creates a new ErrorTicket (state = New)
/// and returns a card with the ticket details and available action buttons.
/// </summary>
public sealed class TicketCommand : ICommand
{
    private readonly ITicketRepository _repo;
    private readonly TicketWorkflow    _workflow;

    public string Name => "ticket";
    public string Description => "Creates a new error ticket. Usage: `/ticket <description>`";

    public TicketCommand(ITicketRepository repo, TicketWorkflow workflow)
    {
        _repo = repo;
        _workflow = workflow;
    }

    public bool CanHandle(string input)
        => input.StartsWith("/ticket", StringComparison.OrdinalIgnoreCase);

    public async Task<BotResponse> ExecuteAsync(string input)
    {
        var description = input["/ticket".Length..].Trim();

        if (string.IsNullOrWhiteSpace(description))
        {
            return BotResponse.FromText("Usage: `/ticket <description>` — provide a short description of the error.");
        }

        var ticket = new ErrorTicket
        {
            Title = description.Length > 80 ? description[..80] : description,
            Description = description,
            Source = "Manual",
        };

        await _repo.AddAsync(ticket);

        return BotResponse.FromCard(TicketCardBuilder.Build(ticket, _workflow));
    }
}
