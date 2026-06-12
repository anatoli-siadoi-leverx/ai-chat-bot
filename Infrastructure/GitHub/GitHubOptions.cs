namespace Infrastructure.GitHub;

/// <summary>Bound from the "GitHub" configuration section.</summary>
public sealed class GitHubOptions
{
    public const string SectionName = "GitHub";

    /// <summary>Repository owner (user or organisation).</summary>
    public string Owner { get; set; } = string.Empty;

    /// <summary>Repository name.</summary>
    public string Repo { get; set; } = string.Empty;

    /// <summary>Personal access token or GitHub App installation token.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Base branch for fix branches, e.g. "main" or "master".</summary>
    public string DefaultBranch { get; set; } = "main";
}
