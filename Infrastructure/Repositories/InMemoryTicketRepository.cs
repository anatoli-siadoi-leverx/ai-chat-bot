using System.Collections.Concurrent;
using Domain.Repositories;
using Domain.Tickets;

namespace Infrastructure.Repositories;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="ITicketRepository"/>.
/// Intended for development and testing; replace with a database-backed
/// implementation in production.
/// </summary>
public sealed class InMemoryTicketRepository : ITicketRepository
{
    private readonly ConcurrentDictionary<Guid, ErrorTicket> _store = new();

    public Task AddAsync(ErrorTicket ticket)
    {
        _store[ticket.Id] = ticket;

        return Task.CompletedTask;
    }

    public Task<ErrorTicket?> GetByIdAsync(Guid id)
    {
        _store.TryGetValue(id, out var ticket);

        return Task.FromResult(ticket);
    }

    public Task<IList<ErrorTicket>> GetAllAsync()
    {
        IList<ErrorTicket> result = _store.Values
            .OrderByDescending(t => t.CreatedAt)
            .ToList();

        return Task.FromResult(result);
    }

    public Task UpdateAsync(ErrorTicket ticket)
    {
        if (!_store.ContainsKey(ticket.Id))
        {
            throw new KeyNotFoundException($"Ticket {ticket.Id} not found.");
        }

        ticket.UpdatedAt = DateTimeOffset.UtcNow;
        _store[ticket.Id] = ticket;

        return Task.CompletedTask;
    }
}
