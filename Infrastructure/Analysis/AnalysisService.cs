using Domain.Tickets;
using Infrastructure.OpenAi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using Tools.Abstractions;

namespace Infrastructure.Analysis;

/// <summary>
/// Runs an agentic LLM loop with <c>github_read_file</c> and
/// <c>github_search_code</c> tools to analyse a bug report and return
/// a detailed written report.
/// </summary>
public sealed class AnalysisService : IAnalysisService
{
    private readonly ChatClient               _client;
    private readonly ITool                    _repoTool;
    private readonly ITool                    _searchTool;
    private readonly ILogger<AnalysisService> _logger;

    private const int MaxIterations = 8;

    public AnalysisService(
        IOptions<OpenAiOptions>                              openAiOptions,
        [FromKeyedServices("github_read_file")]   ITool      readFileTool,
        [FromKeyedServices("github_search_code")] ITool      searchCodeTool,
        ILogger<AnalysisService>                             logger)
    {
        _client     = new OpenAIClient(openAiOptions.Value.ApiKey)
                          .GetChatClient(openAiOptions.Value.Model);
        _repoTool   = readFileTool;
        _searchTool = searchCodeTool;
        _logger     = logger;
    }

    /// <inheritdoc/>
    public async Task<string> AnalyzeAsync(ErrorTicket ticket, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting analysis for ticket {Id}", ticket.Id);

        const string systemPrompt = """
            You are an expert software engineer analysing bug reports.
            Use the available tools to search the repository for relevant files,
            then read those files to understand the code context.
            After investigating, produce a detailed analysis containing:
              - The probable root cause
              - The file(s) and approximate location that need to change
              - A brief description of what fix is required
            Be specific about file paths.
            """;

        var userPrompt = $"""
            Analyse the following bug report and identify the root cause.

            Bug report:
            {ticket.Description}

            Steps:
            1. Use github_search_code to find files related to the error
            2. Use github_read_file to read the relevant source files
            3. Provide a detailed analysis
            """;

        try
        {
            return await RunLoopAsync(systemPrompt, userPrompt,
                [_repoTool, _searchTool], ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Analysis failed for ticket {Id}", ticket.Id);
            return $"Analysis failed: {ex.Message}";
        }
    }

    // ── Agent loop ────────────────────────────────────────────────────────────

    private async Task<string> RunLoopAsync(
        string systemPrompt, string userPrompt,
        IReadOnlyList<ITool> tools,
        CancellationToken ct)
    {
        List<ChatMessage> messages =
        [
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userPrompt),
        ];

        var options = BuildOptions(tools);

        for (int i = 0; i < MaxIterations; i++)
        {
            _logger.LogDebug("Analysis iteration {I}/{Max}", i + 1, MaxIterations);

            var completion = await _client.CompleteChatAsync(messages, options, cancellationToken: ct);
            var reason     = completion.Value.FinishReason;

            if (reason == ChatFinishReason.Stop)
                return completion.Value.Content[0].Text;

            if (reason == ChatFinishReason.ToolCalls)
            {
                messages.Add(new AssistantChatMessage(completion.Value));

                foreach (var call in completion.Value.ToolCalls)
                {
                    var tool   = tools.FirstOrDefault(t => t.Name == call.FunctionName);
                    var result = tool is null
                        ? $"Unknown tool: {call.FunctionName}"
                        : await tool.ExecuteAsync(call.FunctionArguments.ToString());

                    _logger.LogDebug("Tool {Tool} → {Len} chars", call.FunctionName, result.Length);
                    messages.Add(new ToolChatMessage(call.Id, result));
                }

                continue;
            }

            return completion.Value.Content.FirstOrDefault()?.Text ?? "(no response)";
        }

        _logger.LogWarning("Analysis hit max iterations ({Max})", MaxIterations);
        return "Analysis timed out — maximum tool-call iterations reached.";
    }

    private static ChatCompletionOptions BuildOptions(IReadOnlyList<ITool> tools)
    {
        var options = new ChatCompletionOptions();
        foreach (var t in tools)
            options.Tools.Add(ChatTool.CreateFunctionTool(
                t.Name, t.Description, BinaryData.FromString(t.InputSchema)));
        return options;
    }
}
