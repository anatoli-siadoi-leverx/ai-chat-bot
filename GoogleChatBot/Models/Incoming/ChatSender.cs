using System.Text.Json.Serialization;

namespace GoogleChatBot.Models.Incoming;

/// <summary>User who sent the message.</summary>
public sealed class ChatSender
{
    /// <summary>Resource name, e.g. "users/12345".</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;
}
