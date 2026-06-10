namespace Infrastructure.OpenAi;

/// <summary>
/// Bound from the "OpenAi" configuration section.
/// Store the real API key in user-secrets or an environment variable — never in appsettings.json.
/// </summary>
public sealed class OpenAiOptions
{
    public const string SectionName = "OpenAi";

    /// <summary>OpenAI API key. Use user-secrets in development: dotnet user-secrets set "OpenAi:ApiKey" "sk-..."</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Chat model name, e.g. "gpt-4o-mini", "gpt-4o".</summary>
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>Maximum tokens in the LLM response.</summary>
    public int MaxTokens { get; set; } = 1024;

    /// <summary>System prompt that shapes the assistant's personality.</summary>
    public string SystemPrompt { get; set; } =
        "You are a helpful AI assistant embedded in Google Chat. " +
        "Be concise, friendly, and format responses using Google Chat markdown (* for bold, _ for italic, ` for code).";
}
