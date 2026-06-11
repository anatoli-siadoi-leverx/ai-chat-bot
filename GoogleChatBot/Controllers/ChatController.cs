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
            "CARD_CLICKED"       => Ok(ToApiResponse(await _handler.OnCardClickedAsync(chatEvent))),
            "REMOVED_FROM_SPACE" => Ok(new { }),
            _                    => Ok(new { })
        };
    }

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
