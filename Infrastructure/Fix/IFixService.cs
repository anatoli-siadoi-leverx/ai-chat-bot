using Domain.Tickets;

namespace Infrastructure.Fix;

/// <summary>
/// Creates a fix branch, runs an LLM agent with GitHub tools to generate and
/// commit a patch, and returns the branch name.
/// </summary>
public interface IFixService
{
    /// <summary>
    /// Creates a dedicated fix branch, uses the LLM to read relevant files
    /// and commit the fix, then returns the branch name.
    /// Never throws — returns a descriptive error string on failure.
    /// </summary>
    /// <returns>The name of the branch where the fix was committed.</returns>
    Task<string> ApplyFixAsync(ErrorTicket ticket, CancellationToken ct = default);
}
