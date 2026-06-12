using Domain.Repositories;
using Domain.Tickets;
using Domain.Workflow;
using Infrastructure.Google;

namespace GoogleChatBot.Services;

/// <summary>
/// Posts status updates and error messages to the Google Chat thread
/// associated with a ticket.
/// </summary>
public sealed class TicketThreadNotifier(
    IGoogleChatApiService chatApi,
    ITicketRepository repo,
    TicketWorkflow workflow,
    ILogger<TicketThreadNotifier> logger) : ITicketThreadNotifier
{
    private static readonly IReadOnlyDictionary<string, string> ActionMessages =
        new Dictionary<string, string>
        {
            ["analyze"] = "🔍 *Analysis queued.* Results will appear here when complete.",
            ["mark_analyzed"] = "✅ *Marked as Analyzed.* Ready to proceed with a fix.",
            ["fix"] = "🔧 *Fix generation queued.* Results will appear here when complete.",
            ["mark_fixed"] = "✅ *Fix applied.*",
            ["close"] = "🔒 *Ticket closed.*",
            ["retry"] = "🔄 *Retry requested.* Ticket reset to New state.",
        };

    public bool HasChatThread(ErrorTicket ticket) =>
        !string.IsNullOrEmpty(ticket.SpaceName) &&
        !string.IsNullOrEmpty(ticket.ThreadName);

    public async Task PostStatusReplyAsync(ErrorTicket ticket, string function)
    {
        if (!HasChatThread(ticket))
        {
            return;
        }

        var message = ActionMessages.TryGetValue(function, out var msg)
            ? msg
            : $"State updated to: *{ticket.State}*";

        try
        {
            await chatApi.PostThreadReplyAsync(ticket.SpaceName!, ticket.ThreadName!, message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to post status reply for ticket {Id}", ticket.Id);
        }
    }

    public async Task MarkFailedAsync(ErrorTicket ticket, string errorMessage)
    {
        try
        {
            workflow.Transition(ticket, TicketState.Failed);
            ticket.UpdatedAt = DateTimeOffset.UtcNow;

            await repo.UpdateAsync(ticket);

            if (HasChatThread(ticket))
            {
                await chatApi.PostThreadReplyAsync(ticket.SpaceName!, ticket.ThreadName!, errorMessage);
            }
        }
        catch (Exception inner)
        {
            logger.LogError(inner, "Failed to mark ticket {Id} as Failed", ticket.Id);
        }
    }
}
