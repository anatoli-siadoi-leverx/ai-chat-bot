using System.Text.Json.Serialization;

namespace GoogleChatBot.Models.Outgoing;

/// <summary>
/// Simple text response sent back to Google Chat.
/// Google Chat expects <c>{ "text": "..." }</c> for plain messages.
/// </summary>
public sealed class ChatResponse
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    public static ChatResponse From(string text) => new() { Text = text };
}
