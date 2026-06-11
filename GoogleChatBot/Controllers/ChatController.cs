using GoogleChatBot.Commands;
using GoogleChatBot.Models.Incoming;
using GoogleChatBot.Models.Outgoing;
using Infrastructure.OpenAi;
using Microsoft.AspNetCore.Mvc;

namespace GoogleChatBot.Controllers;

/// <summary>
/// Receives all events sent by Google Chat via the configured webhook.
/// </summary>
[ApiController]
[Route("chat")]
public sealed class ChatController : ControllerBase
{
    private readonly ILogger<ChatController> _logger;
    private readonly CommandDispatcher       _dispatcher;
    private readonly ILlmService             _llm;
    private readonly ActionController        _actionController;

    public ChatController(
        ILogger<ChatController> logger,
        CommandDispatcher       dispatcher,
        ILlmService             llm,
        ActionController        actionController)
    {
        _logger           = logger;
        _dispatcher       = dispatcher;
        _llm              = llm;
        _actionController = actionController;
    }

    /// <summary>
    /// Main webhook entry point for all Google Chat events.
    /// Google Chat sends a POST with a JSON body on every event.
    /// </summary>
    /// <remarks>
    /// Supported event types: <c>ADDED_TO_SPACE</c>, <c>MESSAGE</c>, <c>CARD_CLICKED</c>, <c>REMOVED_FROM_SPACE</c>.
    /// <br/>
    /// MESSAGE handling priority: slash-command → LLM fallback.
    /// </remarks>
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
            "CARD_CLICKED"       => Ok(await OnCardClickedAsync(chatEvent)),
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

    private async Task<object> OnMessageAsync(ChatEvent chatEvent)
    {
        var text   = chatEvent.Message?.Text?.Trim() ?? string.Empty;
        var sender = chatEvent.Message?.Sender?.DisplayName ?? "Unknown";

        // 1. Slash-commands have priority.
        var commandResult = await _dispatcher.DispatchAsync(text);
        if (commandResult is not null)
            return ToApiResponse(commandResult);

        // 2. Empty message guard.
        if (string.IsNullOrEmpty(text))
            return ChatResponse.From($"Hi **{sender}**! How can I help you?");

        // 3. Fallback: send to LLM.
        return ChatResponse.From(await CallLlmAsync(text));
    }

    private async Task<object> OnCardClickedAsync(ChatEvent chatEvent)
    {
        if (chatEvent.Action is null)
        {
            _logger.LogWarning("CARD_CLICKED received with no action payload");
            return ChatResponse.From("Card action received but no action data found.");
        }

        _logger.LogInformation(
            "Card action: Function={Function}", chatEvent.Action.ActionMethodName);

        var response = await _actionController.HandleAsync(chatEvent.Action);
        return ToApiResponse(response);
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a <see cref="BotResponse"/> to the correct Google Chat wire format:
    /// <see cref="ChatResponse"/> for text or <see cref="CardResponse"/> for cards.
    /// </summary>
    private static object ToApiResponse(BotResponse response) => response switch
    {
        BotResponse.TextOnly t => ChatResponse.From(t.Text),
        BotResponse.Card     c => c.CardResponse,
        _                      => ChatResponse.From(string.Empty)
    };
}
