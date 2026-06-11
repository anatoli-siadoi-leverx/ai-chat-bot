using System.Text.Json.Serialization;

namespace GoogleChatBot.Models.Outgoing;

// ─────────────────────────────────────────────────────────────────────────────
// Root response
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Google Chat Card V2 response envelope.
/// Serialises to <c>{ "cardsV2": [ { "cardId": "...", "card": { ... } } ] }</c>.
/// </summary>
public sealed class CardResponse
{
    [JsonPropertyName("cardsV2")]
    public List<CardV2Wrapper> CardsV2 { get; set; } = [];
}

// ─────────────────────────────────────────────────────────────────────────────
// Card structure
// ─────────────────────────────────────────────────────────────────────────────

public sealed class CardV2Wrapper
{
    [JsonPropertyName("cardId")]
    public string CardId { get; set; } = string.Empty;

    [JsonPropertyName("card")]
    public CardV2 Card { get; set; } = new();
}

public sealed class CardV2
{
    [JsonPropertyName("header")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CardHeader? Header { get; set; }

    [JsonPropertyName("sections")]
    public List<CardSection> Sections { get; set; } = [];
}

public sealed class CardHeader
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("subtitle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Subtitle { get; set; }
}

public sealed class CardSection
{
    [JsonPropertyName("widgets")]
    public List<CardWidget> Widgets { get; set; } = [];
}

// ─────────────────────────────────────────────────────────────────────────────
// Widgets — only ONE property set per widget instance
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A single widget inside a card section.
/// Exactly one property should be non-null; Google Chat renders whichever is present.
/// </summary>
public sealed class CardWidget
{
    [JsonPropertyName("textParagraph")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TextParagraphWidget? TextParagraph { get; set; }

    [JsonPropertyName("buttonList")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ButtonListWidget? ButtonList { get; set; }
}

public sealed class TextParagraphWidget
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

public sealed class ButtonListWidget
{
    [JsonPropertyName("buttons")]
    public List<CardButtonWidget> Buttons { get; set; } = [];
}

// ─────────────────────────────────────────────────────────────────────────────
// Buttons
// ─────────────────────────────────────────────────────────────────────────────

public sealed class CardButtonWidget
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("onClick")]
    public CardButtonOnClick OnClick { get; set; } = new();
}

public sealed class CardButtonOnClick
{
    [JsonPropertyName("action")]
    public CardButtonAction Action { get; set; } = new();
}

public sealed class CardButtonAction
{
    /// <summary>
    /// The function name that Google Chat sends back in
    /// <see cref="GoogleChatBot.Models.Incoming.ChatAction.ActionMethodName"/>
    /// when this button is clicked.
    /// </summary>
    [JsonPropertyName("function")]
    public string Function { get; set; } = string.Empty;

    [JsonPropertyName("parameters")]
    public List<CardActionParameter> Parameters { get; set; } = [];
}

public sealed class CardActionParameter
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}
