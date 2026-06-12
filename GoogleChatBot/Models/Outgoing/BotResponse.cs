namespace GoogleChatBot.Models.Outgoing;

/// <summary>
/// Discriminated union returned by <see cref="GoogleChatBot.Commands.ICommand.ExecuteAsync"/>.
/// <see cref="GoogleChatBot.Controllers.ChatController"/> converts it to the correct
/// Google Chat wire format: plain text or a cardsV2 card.
/// </summary>
public abstract class BotResponse
{
    /// <summary>Plain-text message — serialises to <c>{ "text": "..." }</c>.</summary>
    public sealed class TextOnly(string text) : BotResponse
    {
        public string Text { get; } = text;
    }

    /// <summary>Card message — serialises to <c>{ "cardsV2": [...] }</c>.</summary>
    public sealed class Card(CardResponse card) : BotResponse
    {
        public CardResponse CardResponse { get; } = card;
    }

    public static BotResponse FromText(string text) => new TextOnly(text);
    public static BotResponse FromCard(CardResponse card) => new Card(card);
}
