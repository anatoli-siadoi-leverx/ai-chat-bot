using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace Infrastructure.OpenAi;

/// <summary>
/// Calls OpenAI Chat Completions API via the official .NET SDK.
/// </summary>
public sealed class OpenAiService : ILlmService
{
    private readonly ChatClient _chatClient;
    private readonly OpenAiOptions _options;
    private readonly ILogger<OpenAiService> _logger;

    public OpenAiService(IOptions<OpenAiOptions> options, ILogger<OpenAiService> logger)
    {
        _options    = options.Value;
        _logger     = logger;
        _chatClient = new OpenAIClient(_options.ApiKey).GetChatClient(_options.Model);
    }

    /// <inheritdoc/>
    public async Task<string> CompleteAsync(string userMessage)
    {
        _logger.LogDebug("LLM request | model={Model} | input={Length} chars",
            _options.Model, userMessage.Length);

        List<ChatMessage> messages =
        [
            new SystemChatMessage(_options.SystemPrompt),
            new UserChatMessage(userMessage),
        ];

        var completion = await _chatClient.CompleteChatAsync(messages, new ChatCompletionOptions
        {
            MaxOutputTokenCount = _options.MaxTokens,
        });

        var result = completion.Value.Content[0].Text;

        _logger.LogDebug("LLM response | output={Length} chars", result.Length);

        return result;
    }
}
