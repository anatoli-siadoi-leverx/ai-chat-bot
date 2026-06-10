using System.Text.Json.Serialization;

namespace GoogleChatBot.Models.Incoming;

/// <summary>
/// Payload present when <see cref="ChatEvent.Type"/> is "CARD_CLICKED".
/// Carries the action function name and custom parameters from the card button.
/// </summary>
public sealed class ChatAction
{
    /// <summary>Name of the action function defined on the card button.</summary>
    [JsonPropertyName("actionMethodName")]
    public string ActionMethodName { get; set; } = string.Empty;

    /// <summary>Key-value parameters attached to the button action.</summary>
    [JsonPropertyName("parameters")]
    public List<ChatActionParameter> Parameters { get; set; } = [];
}
