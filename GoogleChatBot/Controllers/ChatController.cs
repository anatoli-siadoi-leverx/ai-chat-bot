using GoogleChatBot.Commands;
using GoogleChatBot.Models.Incoming;
using GoogleChatBot.Models.Outgoing;
using Microsoft.AspNetCore.Mvc;

namespace GoogleChatBot.Controllers;

[ApiController]
[Route("chat")]
public sealed class ChatController : ControllerBase
{
    private readonly ILogger<ChatController> _logger;
    private readonly CommandDispatcher _dispatcher;

    public ChatController(ILogger<ChatController> logger, CommandDispatcher dispatcher)
    {
        _logger     = logger;
        _dispatcher = dispatcher;
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
            "REMOVED_FROM_SPACE" => Ok(new { }),   // no response required
            _                    => Ok(new { })
        };
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private static ChatResponse OnAddedToSpace(ChatEvent chatEvent)
    {
        var spaceLabel = chatEvent.Space?.Type == "DM" ? "a DM" : "this space";
        return ChatResponse.From(
            $"Hello! I'm your AI assistant. I was just added to {spaceLabel}. " +
            "Type `/help` to see what I can do.");
    }

    private async Task<ChatResponse> OnMessageAsync(ChatEvent chatEvent)
    {
        var text   = chatEvent.Message?.Text?.Trim() ?? string.Empty;
        var sender = chatEvent.Message?.Sender?.DisplayName ?? "Unknown";

        // DispatchAsync returns null for non-commands → fall back to plain echo.
        return ChatResponse.From(
            await _dispatcher.DispatchAsync(text) ?? $"Hi **{sender}**! You said: \"{text}\"");
    }

    private ChatResponse OnCardClicked(ChatEvent chatEvent)
    {
        var action = chatEvent.Action?.ActionMethodName ?? "(unknown)";
        _logger.LogInformation("Card action triggered: {Action}", action);

        // Placeholder — interactive card handling is added in Stage 8.
        return ChatResponse.From($"Action \"{action}\" received. (Not yet implemented)");
    }
}
