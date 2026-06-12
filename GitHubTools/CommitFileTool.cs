using System.Text.Json;
using Infrastructure.GitHub;
using Tools.Abstractions;

namespace GitHubTools;

/// <summary>
/// LLM tool that creates or updates a file on a specific branch in the GitHub repository.
/// Input JSON: <c>{ "branch": "fix/abc", "path": "src/X.cs", "content": "...", "message": "fix: ..." }</c>
/// </summary>
public sealed class CommitFileTool : ITool
{
    private readonly IGitHubService _github;

    public CommitFileTool(IGitHubService github) => _github = github;

    public string Name        => "github_commit_file";
    public string Description => "Creates or updates a file on a specific branch in the GitHub repository. Use this to commit the fix after reading and modifying the relevant source file.";
    public string InputSchema => """
        {
          "type": "object",
          "properties": {
            "branch":  { "type": "string", "description": "Target branch name" },
            "path":    { "type": "string", "description": "File path relative to repository root" },
            "content": { "type": "string", "description": "Full new content of the file (not a diff — the complete replacement)" },
            "message": { "type": "string", "description": "Commit message, e.g. 'fix: handle null reference in PaymentService'" }
          },
          "required": ["branch", "path", "content", "message"]
        }
        """;

    public async Task<string> ExecuteAsync(string input)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(input) ? "{}" : input);
        var root      = doc.RootElement;

        var branch  = root.TryGetProperty("branch",  out var br) ? br.GetString() ?? "" : "";
        var path    = root.TryGetProperty("path",    out var p)  ? p.GetString()  ?? "" : "";
        var content = root.TryGetProperty("content", out var c)  ? c.GetString()  ?? "" : "";
        var message = root.TryGetProperty("message", out var m)  ? m.GetString()  ?? $"fix: update {path}" : $"fix: update {path}";

        if (string.IsNullOrEmpty(branch) || string.IsNullOrEmpty(path) || string.IsNullOrEmpty(content))
            return "Error: 'branch', 'path', and 'content' are required.";

        await _github.CommitFileAsync(branch, path, content, message);
        return $"Successfully committed '{path}' to branch '{branch}'.";
    }
}
