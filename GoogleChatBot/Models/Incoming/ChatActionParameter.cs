using System.Text.Json.Serialization;

namespace GoogleChatBot.Models.Incoming;

/// <summary>Single key/value parameter passed from a card button click.</summary>
public sealed class ChatActionParameter
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}
