using System.Text.Json.Serialization;

namespace GoogleChatBot.Models.Incoming;

/// <summary>Represents an individual message inside a <see cref="ChatEvent"/>.</summary>
public sealed class ChatMessage
{
    /// <summary>Resource name of the message, e.g. "spaces/xxx/messages/yyy".</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Plain-text body of the message.</summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("sender")]
    public ChatSender? Sender { get; set; }
}
