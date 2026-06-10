using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using Tools;

namespace Infrastructure.OpenAi;

/// <summary>
/// LLM service that runs an agent loop: the model can call registered tools
/// before producing a final answer.
///
/// Flow per request:
///   1. Build messages: [system, user]
///   2. Build ChatTool definitions from ToolRegistry
///   3. Call OpenAI
///   4a. FinishReason.Stop     → return text
///   4b. FinishReason.ToolCalls → execute tools, append results, go to 3
///   5. Repeat up to MaxIterations
/// </summary>
public sealed class AgentService : ILlmService
{
    private readonly ChatClient _chatClient;
    private readonly ToolRegistry _toolRegistry;
    private readonly OpenAiOptions _options;
    private readonly ILogger<AgentService> _logger;

    private const int MaxIterations = 5;

    public AgentService(
        IOptions<OpenAiOptions> options,
        ToolRegistry toolRegistry,
        ILogger<AgentService> logger)
    {
        _options      = options.Value;
        _toolRegistry = toolRegistry;
        _logger       = logger;
        _chatClient   = new OpenAIClient(_options.ApiKey).GetChatClient(_options.Model);
    }

    /// <inheritdoc/>
    public async Task<string> CompleteAsync(string userMessage)
    {
        _logger.LogDebug("Agent request | model={Model} | input={Len} chars",
            _options.Model, userMessage.Length);

        List<ChatMessage> messages =
        [
            new SystemChatMessage(_options.SystemPrompt),
            new UserChatMessage(userMessage),
        ];

        // Build tool definitions once — reused across all iterations.
        var completionOptions = BuildCompletionOptions();

        for (int i = 0; i < MaxIterations; i++)
        {
            _logger.LogDebug("Agent iteration {Iter}/{Max}", i + 1, MaxIterations);

            var completion = await _chatClient.CompleteChatAsync(messages, completionOptions);
            var reason     = completion.Value.FinishReason;

            _logger.LogDebug("Finish reason: {Reason}", reason);

            if (reason == ChatFinishReason.Stop)
                return completion.Value.Content[0].Text;

            if (reason == ChatFinishReason.ToolCalls)
            {
                // Add assistant's turn (contains the tool call requests).
                messages.Add(new AssistantChatMessage(completion.Value));

                // Execute every requested tool and append its result.
                foreach (var toolCall in completion.Value.ToolCalls)
                {
                    var result = await ExecuteToolCallAsync(toolCall);
                    messages.Add(new ToolChatMessage(toolCall.Id, result));
                }

                continue; // let the model see the tool results
            }

            // ContentFilter or other terminal reason — return whatever text exists.
            return completion.Value.Content.FirstOrDefault()?.Text
                   ?? "I was unable to complete the request.";
        }

        _logger.LogWarning("Agent loop hit max iterations ({Max})", MaxIterations);
        return "I'm sorry, I was unable to complete your request within the allowed steps.";
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private ChatCompletionOptions BuildCompletionOptions()
    {
        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = _options.MaxTokens,
        };

        foreach (var tool in _toolRegistry.GetAll())
        {
            options.Tools.Add(ChatTool.CreateFunctionTool(
                functionName:        tool.Name,
                functionDescription: tool.Description,
                functionParameters:  BinaryData.FromString(tool.InputSchema)));
        }

        return options;
    }

    private async Task<string> ExecuteToolCallAsync(ChatToolCall toolCall)
    {
        var tool = _toolRegistry.Find(toolCall.FunctionName);

        if (tool is null)
        {
            _logger.LogWarning("LLM requested unknown tool: {Tool}", toolCall.FunctionName);
            return $"Error: tool '{toolCall.FunctionName}' not found.";
        }

        var argsJson = toolCall.FunctionArguments.ToString();
        _logger.LogInformation("Tool call: {Tool}({Args})", toolCall.FunctionName, argsJson);

        try
        {
            // Tools take a plain string. For single-parameter tools (e.g. "name") we
            // extract the first string property. No-parameter tools receive empty string.
            var input  = ExtractFirstStringArg(argsJson);
            var result = await tool.ExecuteAsync(input);

            _logger.LogInformation("Tool {Tool} → {Result}", toolCall.FunctionName, result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool {Tool} threw an exception", toolCall.FunctionName);
            return $"Error executing {toolCall.FunctionName}: {ex.Message}";
        }
    }

    /// <summary>
    /// Extracts the first string-valued property from a JSON object.
    /// <c>{"name":"Alice"}</c> → "Alice", <c>{}</c> → "".
    /// </summary>
    private static string ExtractFirstStringArg(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Trim() is "{}" or "{ }")
            return string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                    return prop.Value.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            return json; // not valid JSON — pass through as-is
        }

        return string.Empty;
    }
}
