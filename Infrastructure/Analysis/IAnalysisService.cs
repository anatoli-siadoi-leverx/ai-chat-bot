using Domain.Tickets;

namespace Infrastructure.Analysis;

/// <summary>
/// Runs an LLM agent with GitHub read tools to produce a written analysis
/// of an <see cref="ErrorTicket"/>.
/// </summary>
public interface IAnalysisService
{
    /// <summary>
    /// Analyses the bug described in <paramref name="ticket"/> by searching
    /// the GitHub repository for relevant code and reading affected files.
    /// Returns a human-readable analysis report.
    /// Never throws — returns a descriptive error string on failure.
    /// </summary>
    Task<string> AnalyzeAsync(ErrorTicket ticket, CancellationToken ct = default);
}
