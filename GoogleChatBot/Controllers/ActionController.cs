using Domain.Repositories;
using Domain.Tickets;
using Domain.Workflow;
using GoogleChatBot.Cards;
using GoogleChatBot.Models.Incoming;
using GoogleChatBot.Models.Outgoing;
using Infrastructure.Google;

namespace GoogleChatBot.Controllers;

/// <summary>
/// Handles <c>CARD_CLICKED</c> events: applies the requested workflow transition
/// to the ticket, posts a status reply in the notification thread, and returns
/// an updated card so <see cref="ChatController"/> can update the original card in-place.
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

    // Text posted to the notification thread after each transition.
    private static readonly IReadOnlyDictionary<string, string> ActionThreadMessages =
        new Dictionary<string, string>
        {
            ["analyze"]       = "🔍 *Analysis queued.* Results will appear here when complete.",
            ["mark_analyzed"] = "✅ *Marked as Analyzed.* Ready to proceed with a fix.",
            ["fix"]           = "🔧 *Fix generation queued.* Results will appear here when complete.",
            ["mark_fixed"]    = "✅ *Fix applied.*",
            ["close"]         = "🔒 *Ticket closed.*",
            ["retry"]         = "🔄 *Retry requested.* Ticket reset to New state.",
        };

    private readonly ITicketRepository         _repo;
    private readonly TicketWorkflow            _workflow;
    private readonly IGoogleChatApiService     _chatApi;
    private readonly ILogger<ActionController> _logger;

    public ActionController(
        ITicketRepository         repo,
        TicketWorkflow            workflow,
        IGoogleChatApiService     chatApi,
        ILogger<ActionController> logger)
    {
        _repo     = repo;
        _workflow = workflow;
        _chatApi  = chatApi;
        _logger   = logger;
    }

    /// <summary>
    /// Processes a card button click: loads the ticket, transitions its state,
    /// posts a thread reply, and returns a refreshed card for in-place update.
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

            // Post status message into the notification thread (non-blocking on failure)
            await PostThreadReplyAsync(ticket, function);

            // Return updated card — ChatController will send it as UPDATE_MESSAGE
            return BotResponse.FromCard(TicketCardBuilder.Build(ticket, _workflow));
        }
        catch (WorkflowException ex)
        {
            _logger.LogWarning(ex, "Workflow transition rejected for ticket {Id}", ticketId);
            return BotResponse.FromText($"Cannot perform this action: {ex.Message}");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task PostThreadReplyAsync(ErrorTicket ticket, string function)
    {
        // Only tickets created via Drive watcher have SpaceName + ThreadName
        if (string.IsNullOrEmpty(ticket.SpaceName) || string.IsNullOrEmpty(ticket.ThreadName))
            return;

        var message = ActionThreadMessages.TryGetValue(function, out var msg)
            ? msg
            : $"State updated to: *{ticket.State}*";

        try
        {
            await _chatApi.PostThreadReplyAsync(ticket.SpaceName, ticket.ThreadName, message);
            _logger.LogInformation(
                "Thread reply posted for ticket {Id} ({Function})", ticket.Id, function);
        }
        catch (Exception ex)
        {
            // Non-fatal — card update still succeeds even if thread reply fails
            _logger.LogWarning(ex, "Failed to post thread reply for ticket {Id}", ticket.Id);
        }
    }
}
