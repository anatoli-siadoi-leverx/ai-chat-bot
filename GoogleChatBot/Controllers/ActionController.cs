using Domain.Repositories;
using Domain.Tickets;
using Domain.Workflow;
using GoogleChatBot.Cards;
using GoogleChatBot.Models.Incoming;
using GoogleChatBot.Models.Outgoing;
using GoogleChatBot.Services;

namespace GoogleChatBot.Controllers;

/// <summary>
/// Handles <c>CARD_CLICKED</c> events: applies the workflow transition immediately,
/// returns an updated card (via <c>UPDATE_MESSAGE</c>), and — for the <c>analyze</c>
/// and <c>fix</c> actions — fires a background task via <see cref="ITicketPipelineRunner"/>.
/// Thread notifications are delegated to <see cref="ITicketThreadNotifier"/>.
/// </summary>
public sealed class ActionController(
    ITicketRepository repo,
    TicketWorkflow workflow,
    ITicketPipelineRunner pipelineRunner,
    ITicketThreadNotifier threadNotifier,
    ILogger<ActionController> logger)
{
    private static readonly IReadOnlyDictionary<string, TicketState> ActionToState =
        new Dictionary<string, TicketState>
        {
            ["analyze"] = TicketState.Analyzing,
            ["mark_analyzed"] = TicketState.Analyzed,
            ["fix"] = TicketState.Fixing,
            ["mark_fixed"] = TicketState.Fixed,
            ["close"] = TicketState.Closed,
            ["retry"] = TicketState.New,
        };

    public async Task<BotResponse> HandleAsync(ChatAction action)
    {
        var function = action.ActionMethodName;
        var ticketIdRaw = action.Parameters.FirstOrDefault(p => p.Key == "ticketId")?.Value;

        if (!Guid.TryParse(ticketIdRaw, out var ticketId))
        {
            logger.LogWarning("CARD_CLICKED: missing/invalid ticketId. Function={Function}", function);

            return BotResponse.FromText($"Action '{function}' could not be processed: no ticketId.");
        }

        if (!ActionToState.TryGetValue(function, out var targetState))
        {
            logger.LogWarning("CARD_CLICKED: unknown function '{Function}'", function);

            return BotResponse.FromText($"Unknown action: `{function}`.");
        }

        var ticket = await repo.GetByIdAsync(ticketId);

        if (ticket is null)
        {
            return BotResponse.FromText($"Ticket `{ticketId}` not found.");
        }

        try
        {
            workflow.Transition(ticket, targetState);

            await repo.UpdateAsync(ticket);

            logger.LogInformation("Ticket {Id} → {State}", ticket.Id, ticket.State);

            await threadNotifier.PostStatusReplyAsync(ticket, function);

            if (function == "analyze" && threadNotifier.HasChatThread(ticket))
            {
                _ = pipelineRunner.RunAnalysisAsync(ticket);
            }
            else if (function == "fix" && threadNotifier.HasChatThread(ticket))
            {
                _ = pipelineRunner.RunFixAsync(ticket);
            }

            return BotResponse.FromCard(TicketCardBuilder.Build(ticket, workflow));
        }
        catch (WorkflowException ex)
        {
            logger.LogWarning(ex, "Workflow transition rejected for ticket {Id}", ticketId);

            return BotResponse.FromText($"Cannot perform this action: {ex.Message}");
        }
    }
}
