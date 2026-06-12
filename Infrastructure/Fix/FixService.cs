using Domain.Tickets;
using Infrastructure.GitHub;
using Infrastructure.OpenAi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using Tools.Abstractions;

namespace Infrastructure.Fix;

/// <summary>
/// Fix pipeline:
/// <list type="number">
///   <item>Generates a branch name and creates it via <see cref="IGitHubService"/>.</item>
///   <item>Runs an LLM agent with <c>github_read_file</c>, <c>github_search_code</c>
///         and <c>github_commit_file</c> tools so the model can read code and commit the fix.</item>
///   <item>Returns the branch name so the caller can post it to the Chat thread.</item>
/// </list>
/// The branch is created before the LLM runs; the model is told the branch name in the prompt
/// so it only needs to commit (no CreateBranch tool required).
/// </summary>
public sealed class FixService : IFixService
{
    private readonly ChatClient _client;
    private readonly IGitHubService _github;
    private readonly ITool _repoTool;
    private readonly ITool _searchTool;
    private readonly ITool _commitTool;
    private readonly ILogger<FixService> _logger;

    private const int MaxIterations = 5;

    public FixService(
        IOptions<OpenAiOptions> openAiOptions,
        IGitHubService github,
        [FromKeyedServices("github_read_file")] ITool readFileTool,
        [FromKeyedServices("github_search_code")] ITool searchCodeTool,
        [FromKeyedServices("github_commit_file")] ITool commitFileTool,
        ILogger<FixService> logger)
    {
        _client = new OpenAIClient(openAiOptions.Value.ApiKey).GetChatClient(openAiOptions.Value.Model);
        _github = github;
        _repoTool = readFileTool;
        _searchTool = searchCodeTool;
        _commitTool = commitFileTool;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<string> ApplyFixAsync(ErrorTicket ticket, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting fix pipeline for ticket {Id}", ticket.Id);

        var branchName = GenerateBranchName(ticket);

        try
        {
            // Create the branch first so the LLM can commit to it directly.
            await _github.CreateBranchAsync(branchName, ct: ct);

            _logger.LogInformation("Fix branch {Branch} created for ticket {Id}", branchName, ticket.Id);

            var systemPrompt = $"""
                You are an expert software engineer fixing a bug.
                The fix branch '{branchName}' has already been created for you.
                Use the available tools to:
                  1. Search for and read the relevant source files
                  2. Understand exactly what needs to change based on the analysis
                  3. Commit the fixed file(s) to branch '{branchName}' using github_commit_file
                Write minimal, clean changes. Do not refactor unrelated code.
                """;

            var userPrompt = $"""
                Fix the following bug.

                Bug report:
                {ticket.Description}

                Prior analysis:
                {(string.IsNullOrWhiteSpace(ticket.AnalysisResult)
                    ? "(no prior analysis — investigate the codebase yourself)"
                    : ticket.AnalysisResult)}

                Use github_search_code and github_read_file to locate the problem,
                then github_commit_file to commit the fix to branch '{branchName}'.
                When done, briefly summarise what you changed.
                """;

            await RunLoopAsync(systemPrompt, userPrompt, [_repoTool, _searchTool, _commitTool], ct);

            _logger.LogInformation("Fix agent loop completed for ticket {Id}", ticket.Id);

            return branchName;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fix pipeline failed for ticket {Id}", ticket.Id);

            throw;
        }
    }

    private async Task RunLoopAsync(
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
            _logger.LogDebug("Fix iteration {I}/{Max}", i + 1, MaxIterations);

            var completion = await _client.CompleteChatAsync(messages, options, cancellationToken: ct);

            var reason = completion.Value.FinishReason;

            if (reason == ChatFinishReason.Stop)
            {
                return;
            }

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

            return; // ContentFilter or other terminal reason
        }

        _logger.LogWarning("Fix agent loop hit max iterations ({Max})", MaxIterations);
    }

    private static ChatCompletionOptions BuildOptions(IReadOnlyList<ITool> tools)
    {
        var options = new ChatCompletionOptions();

        foreach (var t in tools)
        {
            options.Tools.Add(ChatTool.CreateFunctionTool(
                t.Name, t.Description, BinaryData.FromString(t.InputSchema)));
        }
            
        return options;
    }

    private static string GenerateBranchName(ErrorTicket ticket)
    {
        var shortId = ticket.Id.ToString("N")[..8];
        var slug = new string(
            ticket.Title
                  .ToLowerInvariant()
                  .Select(c => char.IsLetterOrDigit(c) ? c : '-')
                  .Take(30)
                  .ToArray())
            .Trim('-');

        return $"fix/{shortId}-{slug}";
    }
}
