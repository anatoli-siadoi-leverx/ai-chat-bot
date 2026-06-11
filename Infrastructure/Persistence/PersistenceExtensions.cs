using Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Persistence;

/// <summary>
/// Extension methods for registering the SQLite ticket store in any host
/// (Worker <em>and</em> GoogleChatBot share the same file).
/// </summary>
public static class PersistenceExtensions
{
    /// <summary>
    /// Registers <see cref="AppDbContext"/> via <see cref="IDbContextFactory{TContext}"/>
    /// and <see cref="SqliteTicketRepository"/> as the <see cref="ITicketRepository"/>.
    /// Call <see cref="EnsureDatabase"/> after building the host to create the schema.
    /// </summary>
    public static IServiceCollection AddSqliteTickets(
        this IServiceCollection services,
        string                  connectionString)
    {
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddSingleton<ITicketRepository, SqliteTicketRepository>();

        return services;
    }

    /// <summary>
    /// Ensures the SQLite database and schema exist, and enables WAL journal mode
    /// for safe concurrent access from multiple processes.
    /// Call once right after <c>host.Build()</c> / <c>app.Build()</c>.
    /// </summary>
    public static void EnsureDatabase(this IServiceProvider services)
    {
        var factory = services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        using var ctx = factory.CreateDbContext();

        ctx.Database.EnsureCreated();

        // WAL mode: allows concurrent reads while a write is in progress —
        // critical when Worker writes and GoogleChatBot reads simultaneously.
        ctx.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    }
}
