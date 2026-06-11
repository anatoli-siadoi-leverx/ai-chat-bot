using Domain.Repositories;
using Domain.Tickets;
using Domain.Workflow;
using GoogleChatBot.Cards;
using GoogleChatBot.Models.Incoming;
using GoogleChatBot.Models.Outgoing;

namespace GoogleChatBot.Controllers;

/// <summary>
/// Handles <c>CARD_CLICKED</c> events: applies the requested workflow transition
/// to the ticket and returns an updated card (or a text error on failure).
/// Injected into <see cref="ChatController"/> — not an HTTP controller itself,
/// since all Google Chat events arrive at a single webhook endpoint.
/// </summary>
public sealed class ActionController
{
    // Maps the action function name (from button click) to the target TicketState.
    private static readonly IReadOnlyDictionary<string, TicketState> ActionToState =
        new Dictionary<string, TicketState>
        {
            ["analyze"]       = TicketState.Analyzing,
            ["mark_analyzed"] = TicketState.Analyzed,
            ["fix"]           = TicketState.Fixing,
            ["mark_fixed"]    = TicketState.Fixed,
            ["close"]         = TicketState.Closed,
            ["retry"]         = TicketState.New,
        };

    private readonly ITicketRepository        _repo;
    private readonly TicketWorkflow           _workflow;
    private readonly ILogger<ActionController> _logger;

    public ActionController(
        ITicketRepository         repo,
        TicketWorkflow            workflow,
        ILogger<ActionController> logger)
    {
        _repo     = repo;
        _workflow = workflow;
        _logger   = logger;
    }

    /// <summary>
    /// Processes a card button click: loads the ticket, transitions its state,
    /// persists the change, and returns a refreshed card.
    /// </summary>
    public async Task<BotResponse> HandleAsync(ChatAction action)
    {
        var function    = action.ActionMethodName;
        var ticketIdRaw = action.Parameters.FirstOrDefault(p => p.Key == "ticketId")?.Value;

        if (!Guid.TryParse(ticketIdRaw, out var ticketId))
        {
            _logger.LogWarning(
                "CARD_CLICKED: missing or invalid ticketId. Function={Function}", function);
            return BotResponse.FromText($"Action '{function}' could not be processed: no ticketId.");
        }

        if (!ActionToState.TryGetValue(function, out var targetState))
        {
            _logger.LogWarning("CARD_CLICKED: unknown function '{Function}'", function);
            return BotResponse.FromText($"Unknown action: `{function}`.");
        }

        var ticket = await _repo.GetByIdAsync(ticketId);
        if (ticket is null)
            return BotResponse.FromText($"Ticket `{ticketId}` not found.");

        try
        {
            _workflow.Transition(ticket, targetState);
            await _repo.UpdateAsync(ticket);

            _logger.LogInformation(
                "Ticket {Id} transitioned to {State}", ticket.Id, ticket.State);

            return BotResponse.FromCard(TicketCardBuilder.Build(ticket, _workflow));
        }
        catch (WorkflowException ex)
        {
            _logger.LogWarning(ex, "Workflow transition rejected for ticket {Id}", ticketId);
            return BotResponse.FromText($"Cannot perform this action: {ex.Message}");
        }
    }
}
