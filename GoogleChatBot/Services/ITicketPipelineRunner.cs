using Domain.Tickets;

namespace GoogleChatBot.Services;

/// <summary>
/// Runs the long-running LLM pipelines (analysis and fix) for a ticket.
/// Intended to be fired as a background task from <c>ActionController</c>.
/// </summary>
public interface ITicketPipelineRunner
{
    /// <summary>
    /// Runs the analysis LLM pipeline for <paramref name="ticket"/>,
    /// saves the result, transitions the ticket to <see cref="TicketState.Analyzed"/>,
    /// and posts the report to the Chat thread.
    /// </summary>
    Task RunAnalysisAsync(ErrorTicket ticket);

    /// <summary>
    /// Runs the fix LLM pipeline for <paramref name="ticket"/>,
    /// saves the branch name, transitions the ticket to <see cref="TicketState.Fixed"/>,
    /// posts the branch name to the Chat thread, and moves the Drive file to Done/.
    /// </summary>
    Task RunFixAsync(ErrorTicket ticket);
}
