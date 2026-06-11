using Domain.Repositories;
using Domain.Tickets;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

/// <summary>
/// <see cref="ITicketRepository"/> backed by SQLite via EF Core.
/// Uses <see cref="IDbContextFactory{TContext}"/> so a fresh <see cref="AppDbContext"/>
/// is created per operation — safe for concurrent access from both Worker and GoogleChatBot.
/// </summary>
public sealed class SqliteTicketRepository(IDbContextFactory<AppDbContext> factory)
    : ITicketRepository
{
    public async Task AddAsync(ErrorTicket ticket)
    {
        await using var ctx = await factory.CreateDbContextAsync();
        ctx.Tickets.Add(ticket);
        await ctx.SaveChangesAsync();
    }

    public async Task<ErrorTicket?> GetByIdAsync(Guid id)
    {
        await using var ctx = await factory.CreateDbContextAsync();
        return await ctx.Tickets.FindAsync(id);
    }

    public async Task<IList<ErrorTicket>> GetAllAsync()
    {
        await using var ctx = await factory.CreateDbContextAsync();
        return await ctx.Tickets.OrderByDescending(t => t.CreatedAt).ToListAsync();
    }

    public async Task UpdateAsync(ErrorTicket ticket)
    {
        await using var ctx = await factory.CreateDbContextAsync();
        ctx.Tickets.Update(ticket);
        await ctx.SaveChangesAsync();
    }
}
