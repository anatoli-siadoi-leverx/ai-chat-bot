using System.Text.Json;
using Infrastructure.GitHub;
using Tools.Abstractions;

namespace GitHubTools;

/// <summary>
/// LLM tool that searches the GitHub repository for code matching a query.
/// Returns up to 10 matching file paths.
/// Input JSON: <c>{ "query": "NullReferenceException PaymentService" }</c>
/// </summary>
public sealed class GitHubSearchTool : ITool
{
    private readonly IGitHubService _github;

    public GitHubSearchTool(IGitHubService github) => _github = github;

    public string Name        => "github_search_code";
    public string Description => "Searches the GitHub repository for code matching a query. Returns a list of file paths that contain the search terms. Use this to find which files are relevant to a bug.";
    public string InputSchema => """
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "Search query, e.g. 'NullReferenceException PaymentService' or 'class PaymentService'" }
          },
          "required": ["query"]
        }
        """;

    public async Task<string> ExecuteAsync(string input)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(input) ? "{}" : input);
        var query     = doc.RootElement.TryGetProperty("query", out var q) ? q.GetString() ?? "" : input;

        if (string.IsNullOrEmpty(query))
            return "Error: 'query' is required.";

        return await _github.SearchCodeAsync(query);
    }
}
