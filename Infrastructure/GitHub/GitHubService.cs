using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;

namespace Infrastructure.GitHub;

/// <summary>
/// Octokit-backed implementation of <see cref="IGitHubService"/>.
/// Uses a Personal Access Token stored in <see cref="GitHubOptions.Token"/>.
/// </summary>
public sealed class GitHubService : IGitHubService
{
    private readonly GitHubClient                _client;
    private readonly GitHubOptions               _options;
    private readonly ILogger<GitHubService>      _logger;

    public GitHubService(
        IOptions<GitHubOptions>      options,
        ILogger<GitHubService>       logger)
    {
        _options = options.Value;
        _logger  = logger;
        _client  = new GitHubClient(new ProductHeaderValue("AiChatBot"))
        {
            Credentials = new Credentials(_options.Token)
        };
    }

    /// <inheritdoc/>
    public async Task<string> ReadFileAsync(
        string path, string? branch = null, CancellationToken ct = default)
    {
        try
        {
            var contents = string.IsNullOrEmpty(branch)
                ? await _client.Repository.Content
                    .GetAllContents(_options.Owner, _options.Repo, path)
                : await _client.Repository.Content
                    .GetAllContentsByRef(_options.Owner, _options.Repo, path, branch);

            var file = contents.FirstOrDefault();
            return file?.Content ?? "(binary or empty file)";
        }
        catch (NotFoundException)
        {
            return $"File not found: {path}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read GitHub file {Path}", path);
            return $"Error reading file: {ex.Message}";
        }
    }

    /// <inheritdoc/>
    public async Task<string> SearchCodeAsync(string query, CancellationToken ct = default)
    {
        try
        {
            var request = new SearchCodeRequest(query)
            {
                Repos = new RepositoryCollection { $"{_options.Owner}/{_options.Repo}" }
            };

            var result = await _client.Search.SearchCode(request);

            if (result.Items.Count == 0)
                return "No matching files found.";

            var lines = result.Items
                .Take(10)
                .Select(i => $"- {i.Path}");

            return string.Join('\n', lines);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GitHub code search failed: {Query}", query);
            return $"Search error: {ex.Message}";
        }
    }

    /// <inheritdoc/>
    public async Task CreateBranchAsync(
        string branchName, string? baseBranch = null, CancellationToken ct = default)
    {
        var @base = baseBranch ?? _options.DefaultBranch;
        try
        {
            var baseRef = await _client.Git.Reference.Get(
                _options.Owner, _options.Repo, $"heads/{@base}");

            await _client.Git.Reference.Create(
                _options.Owner, _options.Repo,
                new NewReference($"refs/heads/{branchName}", baseRef.Object.Sha));

            _logger.LogInformation("Created branch {Branch} from {Base}", branchName, @base);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create branch {Branch}", branchName);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task CommitFileAsync(
        string branch, string path, string content, string commitMessage,
        CancellationToken ct = default)
    {
        try
        {
            // Check if the file already exists (needed for the SHA when updating)
            RepositoryContent? existing = null;
            try
            {
                var existingContents = await _client.Repository.Content
                    .GetAllContentsByRef(_options.Owner, _options.Repo, path, branch);
                existing = existingContents.FirstOrDefault();
            }
            catch (NotFoundException) { /* new file */ }

            if (existing is not null)
            {
                await _client.Repository.Content.UpdateFile(
                    _options.Owner, _options.Repo, path,
                    new UpdateFileRequest(commitMessage, content, existing.Sha, branch));
            }
            else
            {
                await _client.Repository.Content.CreateFile(
                    _options.Owner, _options.Repo, path,
                    new CreateFileRequest(commitMessage, content, branch));
            }

            _logger.LogInformation("Committed {Path} → {Branch}", path, branch);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to commit {Path} to {Branch}", path, branch);
            throw;
        }
    }
}
