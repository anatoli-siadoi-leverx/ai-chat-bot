namespace Domain.Tickets;

public enum TicketState
{
    New = 0,
    Analyzing = 1,
    Analyzed = 2,
    Fixing = 3,
    Fixed = 4,
    Failed = 5,
    Closed = 6
}
