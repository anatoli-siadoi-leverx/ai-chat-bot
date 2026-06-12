using Domain.Tickets;

namespace GoogleChatBot.Services;

/// <summary>
/// Posts status updates and error messages to the Google Chat thread
/// associated with a ticket.
/// </summary>
public interface ITicketThreadNotifier
{
    /// <summary>Returns true when the ticket has both SpaceName and ThreadName set.</summary>
    bool HasChatThread(ErrorTicket ticket);

    /// <summary>
    /// Posts the canonical status message for <paramref name="function"/>
    /// (e.g. "🔍 Analysis queued…") to the ticket's thread.
    /// No-op if the ticket has no thread.
    /// </summary>
    Task PostStatusReplyAsync(ErrorTicket ticket, string function);

    /// <summary>
    /// Transitions the ticket to <see cref="Domain.Tickets.TicketState.Failed"/>,
    /// persists it, and posts <paramref name="errorMessage"/> to the thread.
    /// </summary>
    Task MarkFailedAsync(ErrorTicket ticket, string errorMessage);
}
