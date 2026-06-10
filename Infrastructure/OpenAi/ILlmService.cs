namespace Infrastructure.OpenAi;

/// <summary>
/// Abstraction over a chat completion LLM.
/// Accepts a single user message; system context is configured via <see cref="OpenAiOptions"/>.
/// </summary>
public interface ILlmService
{
    /// <summary>
    /// Sends <paramref name="userMessage"/> to the LLM and returns the response text.
    /// </summary>
    Task<string> CompleteAsync(string userMessage);
}
