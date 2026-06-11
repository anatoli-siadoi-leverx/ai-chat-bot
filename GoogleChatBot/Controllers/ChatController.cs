using GoogleChatBot.Handlers;
using GoogleChatBot.Models.Incoming;
using GoogleChatBot.Models.Outgoing;
using Microsoft.AspNetCore.Mvc;

namespace GoogleChatBot.Controllers;

/// <summary>
/// Receives all events sent by Google Chat via the configured webhook.
/// Routes each event type to <see cref="IChatEventHandler"/> and converts
/// the result to the correct Google Chat wire format.
/// </summary>
[ApiController]
[Route("chat")]
public sealed class ChatController : ControllerBase
{
    private readonly ILogger<ChatController> _logger;
    private readonly IChatEventHandler       _handler;

    public ChatController(
        ILogger<ChatController> logger,
        IChatEventHandler       handler)
    {
        _logger  = logger;
        _handler = handler;
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
            "ADDED_TO_SPACE"     => Ok(ToApiResponse(_handler.OnAddedToSpace(chatEvent))),
            "MESSAGE"            => Ok(ToApiResponse(await _handler.OnMessageAsync(chatEvent))),
            // CARD_CLICKED: update the original card in-place via UPDATE_MESSAGE.
            // Thread replies are posted proactively inside ActionController.
            "CARD_CLICKED"       => Ok(ToCardClickedApiResponse(await _handler.OnCardClickedAsync(chatEvent))),
            "REMOVED_FROM_SPACE" => Ok(new { }),
            _                    => Ok(new { })
        };
    }

    // ── Wire-format converters ────────────────────────────────────────────────

    /// <summary>
    /// Converts a <see cref="BotResponse"/> to the Google Chat wire format
    /// for regular messages (ADDED_TO_SPACE, MESSAGE events).
    /// </summary>
    private static object ToApiResponse(BotResponse response) => response switch
    {
        BotResponse.TextOnly t => ChatResponse.From(t.Text),
        BotResponse.Card     c => c.CardResponse,
        _                      => ChatResponse.From(string.Empty)
    };

    /// <summary>
    /// Converts a <see cref="BotResponse"/> to the Google Chat wire format
    /// for <c>CARD_CLICKED</c> events.
    /// <list type="bullet">
    ///   <item>Card  → <c>actionResponse.type = UPDATE_MESSAGE</c> — updates the clicked card in-place.</item>
    ///   <item>Text  → <c>actionResponse.type = NEW_MESSAGE</c>    — posts an error as a new message.</item>
    /// </list>
    /// </summary>
    private static object ToCardClickedApiResponse(BotResponse response) => response switch
    {
        BotResponse.Card c => new
        {
            actionResponse = new { type = "UPDATE_MESSAGE" },
            cardsV2        = c.CardResponse.CardsV2
        },
        BotResponse.TextOnly t => new
        {
            actionResponse = new { type = "NEW_MESSAGE" },
            text           = t.Text
        },
        _ => new { actionResponse = new { type = "UPDATE_MESSAGE" } }
    };
}
