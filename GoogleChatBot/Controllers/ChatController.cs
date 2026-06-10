using GoogleChatBot.Commands;
using GoogleChatBot.Models.Incoming;
using GoogleChatBot.Models.Outgoing;
using Infrastructure.OpenAi;
using Microsoft.AspNetCore.Mvc;

namespace GoogleChatBot.Controllers;

[ApiController]
[Route("chat")]
public sealed class ChatController : ControllerBase
{
    private readonly ILogger<ChatController> _logger;
    private readonly CommandDispatcher _dispatcher;
    private readonly ILlmService _llm;

    public ChatController(
        ILogger<ChatController> logger,
        CommandDispatcher dispatcher,
        ILlmService llm)
    {
        _logger     = logger;
        _dispatcher = dispatcher;
        _llm        = llm;
    }

    /// <summary>
    /// Main webhook entry point for all Google Chat events.
    /// Google Chat sends a POST with a JSON body on every event.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> HandleEvent([FromBody] ChatEvent chatEvent)
    {
        _logger.LogInformation(
            "Received event Type={Type} Space={Space}",
            chatEvent.Type,
            chatEvent.Space?.Name ?? "(none)");

        return chatEvent.Type switch
        {
            "ADDED_TO_SPACE"     => Ok(OnAddedToSpace(chatEvent)),
            "MESSAGE"            => Ok(await OnMessageAsync(chatEvent)),
            "CARD_CLICKED"       => Ok(OnCardClicked(chatEvent)),
            "REMOVED_FROM_SPACE" => Ok(new { }),
            _                    => Ok(new { })
        };
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private static ChatResponse OnAddedToSpace(ChatEvent chatEvent)
    {
        var spaceLabel = chatEvent.Space?.Type == "DM" ? "a DM" : "this space";
        return ChatResponse.From(
            $"Hello! I'm your AI assistant. I was just added to {spaceLabel}. " +
            "Type `/help` to see my commands, or just ask me anything.");
    }

    private async Task<ChatResponse> OnMessageAsync(ChatEvent chatEvent)
    {
        var text   = chatEvent.Message?.Text?.Trim() ?? string.Empty;
        var sender = chatEvent.Message?.Sender?.DisplayName ?? "Unknown";

        // 1. Slash-commands have priority.
        var commandResult = await _dispatcher.DispatchAsync(text);
        if (commandResult is not null)
            return ChatResponse.From(commandResult);

        // 2. Empty message guard.
        if (string.IsNullOrEmpty(text))
            return ChatResponse.From($"Hi **{sender}**! How can I help you?");

        // 3. Fallback: send to LLM.
        return ChatResponse.From(await CallLlmAsync(text));
    }

    private async Task<string> CallLlmAsync(string text)
    {
        try
        {
            return await _llm.CompleteAsync(text);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM request failed");
            return "Sorry, I encountered an error while processing your message. Please try again.";
        }
    }

    private ChatResponse OnCardClicked(ChatEvent chatEvent)
    {
        var action = chatEvent.Action?.ActionMethodName ?? "(unknown)";
        _logger.LogInformation("Card action triggered: {Action}", action);

        // Placeholder — interactive card handling added in Stage 8.
        return ChatResponse.From($"Action \"{action}\" received. (Not yet implemented)");
    }
}
