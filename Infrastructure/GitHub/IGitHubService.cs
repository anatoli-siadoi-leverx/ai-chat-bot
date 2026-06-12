namespace Infrastructure.GitHub;

/// <summary>
/// Low-level GitHub operations used by tools and services.
/// Implemented by <see cref="GitHubService"/> via Octokit.
/// </summary>
public interface IGitHubService
{
    /// <summary>Returns the UTF-8 text content of a file at <paramref name="path"/>.</summary>
    Task<string> ReadFileAsync(string path, string? branch = null, CancellationToken ct = default);

    /// <summary>
    /// Searches the repository for code matching <paramref name="query"/>.
    /// Returns up to 10 matching file paths, one per line.
    /// </summary>
    Task<string> SearchCodeAsync(string query, CancellationToken ct = default);

    /// <summary>Creates <paramref name="branchName"/> from <paramref name="baseBranch"/>.</summary>
    Task CreateBranchAsync(string branchName, string? baseBranch = null, CancellationToken ct = default);

    /// <summary>
    /// Creates or updates <paramref name="path"/> on <paramref name="branch"/> with
    /// <paramref name="content"/> and the given <paramref name="commitMessage"/>.
    /// </summary>
    Task CommitFileAsync(
        string branch, string path, string content, string commitMessage,
        CancellationToken ct = default);
}
