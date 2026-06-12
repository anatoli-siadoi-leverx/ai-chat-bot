using System.Text.Json;
using Infrastructure.GitHub;
using Tools.Abstractions;

namespace GitHubTools;

/// <summary>
/// LLM tool that reads a file from the GitHub repository by path.
/// Input JSON: <c>{ "path": "src/Services/PaymentService.cs", "branch": "main" }</c>
/// </summary>
public sealed class GitHubRepoTool : ITool
{
    private readonly IGitHubService _github;

    public GitHubRepoTool(IGitHubService github) => _github = github;

    public string Name        => "github_read_file";
    public string Description => "Reads the full content of a file from the GitHub repository. Use this to inspect source code before analysing or fixing a bug.";
    public string InputSchema => """
        {
          "type": "object",
          "properties": {
            "path":   { "type": "string", "description": "File path relative to repository root, e.g. 'src/Services/PaymentService.cs'" },
            "branch": { "type": "string", "description": "Branch name (optional, defaults to default branch)" }
          },
          "required": ["path"]
        }
        """;

    public async Task<string> ExecuteAsync(string input)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(input) ? "{}" : input);
        var root      = doc.RootElement;
        var path      = root.TryGetProperty("path",   out var p) ? p.GetString() ?? "" : "";
        var branch    = root.TryGetProperty("branch", out var b) ? b.GetString()      : null;

        if (string.IsNullOrEmpty(path))
            return "Error: 'path' is required.";

        return await _github.ReadFileAsync(path, branch);
    }
}
