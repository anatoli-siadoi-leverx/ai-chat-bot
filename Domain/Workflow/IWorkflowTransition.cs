using Domain.Tickets;

namespace Domain.Workflow;

/// <summary>
/// Represents a single allowed transition between two ticket states.
/// </summary>
public interface IWorkflowTransition
{
    TicketState From { get; }
    TicketState To   { get; }
}
