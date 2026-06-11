using Domain.Tickets;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for the AI Chat Bot.
/// Currently contains a single <see cref="Tickets"/> set.
/// The database is auto-created via <c>EnsureCreated()</c> on startup — no migrations needed.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ErrorTicket> Tickets => Set<ErrorTicket>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<ErrorTicket>(e =>
        {
            e.HasKey(t => t.Id);

            // Store enum as integer column
            e.Property(t => t.State).HasConversion<int>();

            // DateTimeOffset stored as TEXT — SQLite default, preserves timezone
            e.Property(t => t.CreatedAt);
            e.Property(t => t.UpdatedAt);
        });
    }
}
