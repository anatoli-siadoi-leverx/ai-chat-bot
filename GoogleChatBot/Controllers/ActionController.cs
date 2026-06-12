using Domain.Repositories;
using Domain.Tickets;
using Domain.Workflow;
using GoogleChatBot.Cards;
using GoogleChatBot.Models.Incoming;
using GoogleChatBot.Models.Outgoing;
using Infrastructure.Analysis;
using Infrastructure.Fix;
using Infrastructure.Google;

namespace GoogleChatBot.Controllers;

/// <summary>
/// Handles <c>CARD_CLICKED</c> events: applies the workflow transition immediately,
/// returns an updated card (via <c>UPDATE_MESSAGE</c>), and — for the <c>analyze</c>
/// and <c>fix</c> actions — fires a background task that runs the LLM pipeline,
/// saves the result, and posts follow-up messages to the notification thread.
/// </summary>
public sealed class ActionController
{
    // Maps action function name → target TicketState for the immediate transition.
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

    // Instant thread status posted right after the transition (before background work).
    private static readonly IReadOnlyDictionary<string, string> ActionThreadMessages =
        new Dictionary<string, string>
        {
            ["analyze"] = "🔍 *Analysis queued.* Results will appear here when complete.",
            ["mark_analyzed"] = "✅ *Marked as Analyzed.* Ready to proceed with a fix.",
            ["fix"] = "🔧 *Fix generation queued.* Results will appear here when complete.",
            ["mark_fixed"] = "✅ *Fix applied.*",
            ["close"] = "🔒 *Ticket closed.*",
            ["retry"] = "🔄 *Retry requested.* Ticket reset to New state.",
        };

    private readonly ITicketRepository _repo;
    private readonly TicketWorkflow _workflow;
    private readonly IGoogleChatApiService _chatApi;
    private readonly IAnalysisService _analysis;
    private readonly IFixService _fix;
    private readonly ILogger<ActionController> _logger;

    public ActionController(
        ITicketRepository repo,
        TicketWorkflow workflow,
        IGoogleChatApiService chatApi,
        IAnalysisService analysis,
        IFixService fix,
        ILogger<ActionController> logger)
    {
        _repo = repo;
        _workflow = workflow;
        _chatApi = chatApi;
        _analysis = analysis;
        _fix = fix;
        _logger = logger;
    }

    /// <summary>
    /// Processes a card button click:
    /// <list type="number">
    ///   <item>Loads the ticket and applies the workflow transition.</item>
    ///   <item>Posts an instant status reply to the notification thread.</item>
    ///   <item>For <c>analyze</c> / <c>fix</c>: fires a background LLM task.</item>
    ///   <item>Returns a refreshed card so <see cref="ChatController"/> can call
    ///         <c>UPDATE_MESSAGE</c> on the clicked card.</item>
    /// </list>
    /// </summary>
    public async Task<BotResponse> HandleAsync(ChatAction action)
    {
        var function = action.ActionMethodName;
        var ticketIdRaw = action.Parameters.FirstOrDefault(p => p.Key == "ticketId")?.Value;

        if (!Guid.TryParse(ticketIdRaw, out var ticketId))
        {
            _logger.LogWarning("CARD_CLICKED: missing/invalid ticketId. Function={Function}", function);

            return BotResponse.FromText($"Action '{function}' could not be processed: no ticketId.");
        }

        if (!ActionToState.TryGetValue(function, out var targetState))
        {
            _logger.LogWarning("CARD_CLICKED: unknown function '{Function}'", function);

            return BotResponse.FromText($"Unknown action: `{function}`.");
        }

        var ticket = await _repo.GetByIdAsync(ticketId);

        if (ticket is null)
        {
            return BotResponse.FromText($"Ticket `{ticketId}` not found.");
        }

        try
        {
            _workflow.Transition(ticket, targetState);

            await _repo.UpdateAsync(ticket);

            _logger.LogInformation("Ticket {Id} → {State}", ticket.Id, ticket.State);

            // Post instant status reply (non-fatal if it fails)
            await PostStatusReplyAsync(ticket, function);

            // Fire background LLM task for actions that need it
            if (function == "analyze" && HasChatThread(ticket))
            {
                _ = RunAnalysisAsync(ticket);
            }
            else if (function == "fix" && HasChatThread(ticket))
            {
                _ = RunFixAsync(ticket);
            }

            // Return refreshed card — ChatController sends it as UPDATE_MESSAGE
            return BotResponse.FromCard(TicketCardBuilder.Build(ticket, _workflow));
        }
        catch (WorkflowException ex)
        {
            _logger.LogWarning(ex, "Workflow transition rejected for ticket {Id}", ticketId);

            return BotResponse.FromText($"Cannot perform this action: {ex.Message}");
        }
    }

    private async Task RunAnalysisAsync(ErrorTicket ticket)
    {
        try
        {
            var report = await _analysis.AnalyzeAsync(ticket);

            ticket.AnalysisResult = report;
            ticket.UpdatedAt  = DateTimeOffset.UtcNow;
            _workflow.Transition(ticket, TicketState.Analyzed);

            await _repo.UpdateAsync(ticket);

            // 1. Post analysis text
            var truncated = report.Length > 3500
                ? report[..3500] + "\n\n_(truncated)_"
                : report;

            await _chatApi.PostThreadReplyAsync(
                ticket.SpaceName!, ticket.ThreadName!,
                $"*Analysis complete:*\n\n{truncated}");

            // 2. Post updated ticket card (Analyzed state + Fix button)
            await _chatApi.PostThreadMessageAsync(
                ticket.SpaceName!, ticket.ThreadName!,
                TicketCardBuilder.Build(ticket, _workflow));

            _logger.LogInformation("Analysis posted for ticket {Id}", ticket.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background analysis failed for ticket {Id}", ticket.Id);

            await MarkFailedAsync(ticket, $"❌ Analysis failed: {ex.Message}");
        }
    }

    private async Task RunFixAsync(ErrorTicket ticket)
    {
        try
        {
            var branchName = await _fix.ApplyFixAsync(ticket);

            ticket.BranchName = branchName;
            ticket.UpdatedAt  = DateTimeOffset.UtcNow;
            _workflow.Transition(ticket, TicketState.Fixed);

            await _repo.UpdateAsync(ticket);

            // 1. Post branch name
            await _chatApi.PostThreadReplyAsync(
                ticket.SpaceName!, ticket.ThreadName!,
                $"🔧 Fix committed to branch `{branchName}`. Review and merge when ready.");

            // 2. Post updated ticket card (Fixed state + Close button)
            await _chatApi.PostThreadMessageAsync(
                ticket.SpaceName!, ticket.ThreadName!,
                TicketCardBuilder.Build(ticket, _workflow));

            _logger.LogInformation("Fix posted for ticket {Id}, branch {Branch}", ticket.Id, branchName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background fix failed for ticket {Id}", ticket.Id);

            await MarkFailedAsync(ticket, $"❌ Fix pipeline failed: {ex.Message}");
        }
    }

    private static bool HasChatThread(ErrorTicket ticket) =>
        !string.IsNullOrEmpty(ticket.SpaceName) &&
        !string.IsNullOrEmpty(ticket.ThreadName);

    private async Task PostStatusReplyAsync(ErrorTicket ticket, string function)
    {
        if (!HasChatThread(ticket))
        {
            return;
        }

        var message = ActionThreadMessages.TryGetValue(function, out var msg)
            ? msg
            : $"State updated to: *{ticket.State}*";

        try
        {
            await _chatApi.PostThreadReplyAsync(ticket.SpaceName!, ticket.ThreadName!, message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to post status reply for ticket {Id}", ticket.Id);
        }
    }

    private async Task MarkFailedAsync(ErrorTicket ticket, string errorMessage)
    {
        try
        {
            _workflow.Transition(ticket, TicketState.Failed);
            ticket.UpdatedAt = DateTimeOffset.UtcNow;

            await _repo.UpdateAsync(ticket);
            await _chatApi.PostThreadReplyAsync(ticket.SpaceName!, ticket.ThreadName!, errorMessage);
        }
        catch (Exception inner)
        {
            _logger.LogError(inner, "Failed to mark ticket {Id} as Failed", ticket.Id);
        }
    }
}
