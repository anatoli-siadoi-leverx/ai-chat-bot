using Domain.Tickets;

namespace Domain.Workflow;

/// <summary>
/// Defines the allowed state transitions for an <see cref="ErrorTicket"/>
/// and applies them in a controlled way.
/// </summary>
public sealed class TicketWorkflow
{
    // All permitted (From → To) transitions.
    private static readonly HashSet<(TicketState From, TicketState To)> AllowedTransitions =
    [
        (TicketState.New,       TicketState.Analyzing),
        (TicketState.Analyzing, TicketState.Analyzed),
        (TicketState.Analyzing, TicketState.Failed),
        (TicketState.Analyzed,  TicketState.Fixing),
        (TicketState.Fixing,    TicketState.Fixed),
        (TicketState.Fixing,    TicketState.Failed),
        (TicketState.Fixed,     TicketState.Closed),
        (TicketState.Failed,    TicketState.New),    // allow retry
    ];

    /// <summary>Returns true if the ticket can move to <paramref name="target"/>.</summary>
    public bool CanTransition(ErrorTicket ticket, TicketState target)
        => AllowedTransitions.Contains((ticket.State, target));

    /// <summary>
    /// Returns all states reachable from <paramref name="current"/>.
    /// Used by Stage 8 to decide which action buttons to render.
    /// </summary>
    public IReadOnlySet<TicketState> GetAvailableTransitions(TicketState current)
        => AllowedTransitions
            .Where(t => t.From == current)
            .Select(t => t.To)
            .ToHashSet();

    /// <summary>
    /// Applies the transition; throws <see cref="WorkflowException"/> if not allowed.
    /// Updates <see cref="ErrorTicket.State"/> and <see cref="ErrorTicket.UpdatedAt"/>.
    /// </summary>
    public void Transition(ErrorTicket ticket, TicketState target)
    {
        if (!CanTransition(ticket, target))
            throw new WorkflowException(
                $"Transition {ticket.State} → {target} is not allowed for ticket {ticket.Id}.");

        ticket.State     = target;
        ticket.UpdatedAt = DateTimeOffset.UtcNow;
    }
}
