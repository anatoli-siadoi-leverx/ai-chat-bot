using Domain.Tickets;
using Domain.Workflow;
using GoogleChatBot.Models.Outgoing;

namespace GoogleChatBot.Cards;

/// <summary>
/// Builds a Google Chat card for an <see cref="ErrorTicket"/>.
/// Action buttons are derived from <see cref="TicketWorkflow.GetAvailableTransitions"/>.
/// </summary>
public static class TicketCardBuilder
{
    // Maps each reachable TicketState to the button label and action function name
    // that will be sent back in CARD_CLICKED → action.actionMethodName.
    private static readonly IReadOnlyDictionary<TicketState, (string Label, string Function)> TransitionButtons =
        new Dictionary<TicketState, (string, string)>
        {
            [TicketState.Analyzing] = ("Analyze",       "analyze"),
            [TicketState.Analyzed]  = ("Mark Analyzed", "mark_analyzed"),
            [TicketState.Fixing]    = ("Start Fix",     "fix"),
            [TicketState.Fixed]     = ("Mark Fixed",    "mark_fixed"),
            [TicketState.Closed]    = ("Close",         "close"),
            [TicketState.New]       = ("Retry",         "retry"),
        };

    /// <summary>
    /// Creates a card showing the ticket details and one action button
    /// per allowed workflow transition.
    /// </summary>
    public static CardResponse Build(ErrorTicket ticket, TicketWorkflow workflow)
    {
        var available  = workflow.GetAvailableTransitions(ticket.State);
        var parameters = new Dictionary<string, string>
        {
            ["ticketId"] = ticket.Id.ToString("D")
        };

        var builder = new CardBuilder()
            .WithCardId($"ticket-{ticket.Id:N}")
            .WithTitle(ticket.Title)
            .WithSubtitle($"State: {ticket.State}  |  Source: {ticket.Source}  |  {ticket.CreatedAt:yyyy-MM-dd HH:mm} UTC")
            .AddParagraph(ticket.Description);

        if (!string.IsNullOrEmpty(ticket.AnalysisResult))
            builder.AddParagraph($"<b>Analysis:</b> {ticket.AnalysisResult}");

        foreach (var state in available)
        {
            if (TransitionButtons.TryGetValue(state, out var btn))
                builder.AddButton(btn.Label, btn.Function, parameters);
        }

        return builder.Build();
    }
}
