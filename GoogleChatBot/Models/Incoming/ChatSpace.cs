using System.Text.Json.Serialization;

namespace GoogleChatBot.Models.Incoming;

/// <summary>The Google Chat space (DM or room) where the event occurred.</summary>
public sealed class ChatSpace
{
    /// <summary>Resource name, e.g. "spaces/AAA".</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>"DM" or "ROOM".</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}
