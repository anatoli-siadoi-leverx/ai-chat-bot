using System.Text.Json.Serialization;
using GoogleChatBot.Models.Incoming;

namespace GoogleChatBot.Models.Incoming;

/// <summary>
/// Root envelope sent by Google Chat to the bot webhook.
/// Docs: https://developers.google.com/chat/api/reference/rest/v1/EventType
/// </summary>
public sealed class ChatEvent
{
    /// <summary>
    /// Event type. Possible values:
    /// "MESSAGE"            — user sent a message
    /// "ADDED_TO_SPACE"     — bot was added to a space or DM
    /// "REMOVED_FROM_SPACE" — bot was removed
    /// "CARD_CLICKED"       — user clicked an interactive card button
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public ChatMessage? Message { get; set; }

    [JsonPropertyName("space")]
    public ChatSpace? Space { get; set; }

    [JsonPropertyName("action")]
    public ChatAction? Action { get; set; }
}
