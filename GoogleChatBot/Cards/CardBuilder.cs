using GoogleChatBot.Models.Outgoing;

namespace GoogleChatBot.Cards;

/// <summary>
/// Fluent builder for a Google Chat Card V2 payload.
/// </summary>
public sealed class CardBuilder
{
    private string  _cardId   = Guid.NewGuid().ToString("N")[..8];
    private string  _title    = string.Empty;
    private string? _subtitle;

    private readonly List<string>       _paragraphs = [];
    private readonly List<ButtonAction> _buttons    = [];

    public CardBuilder WithCardId(string id)       { _cardId    = id;       return this; }
    public CardBuilder WithTitle(string title)     { _title     = title;    return this; }
    public CardBuilder WithSubtitle(string sub)    { _subtitle  = sub;      return this; }
    public CardBuilder AddParagraph(string text)   { _paragraphs.Add(text); return this; }
    public CardBuilder AddButton(ButtonAction btn) { _buttons.Add(btn);     return this; }

    /// <summary>Convenience overload — creates a <see cref="ButtonAction"/> inline.</summary>
    public CardBuilder AddButton(
        string label,
        string functionName,
        IReadOnlyDictionary<string, string> parameters)
        => AddButton(new ButtonAction(label, functionName, parameters));

    /// <summary>Builds and returns the <see cref="CardResponse"/> ready for serialisation.</summary>
    public CardResponse Build()
    {
        var widgets = new List<CardWidget>();

        foreach (var text in _paragraphs)
            widgets.Add(new CardWidget { TextParagraph = new TextParagraphWidget { Text = text } });

        if (_buttons.Count > 0)
        {
            var cardButtons = _buttons
                .Select(b => new CardButtonWidget
                {
                    Text    = b.Label,
                    OnClick = new CardButtonOnClick
                    {
                        Action = new CardButtonAction
                        {
                            Function   = b.FunctionName,
                            Parameters = b.Parameters
                                .Select(p => new CardActionParameter { Key = p.Key, Value = p.Value })
                                .ToList()
                        }
                    }
                })
                .ToList();

            widgets.Add(new CardWidget { ButtonList = new ButtonListWidget { Buttons = cardButtons } });
        }

        return new CardResponse
        {
            CardsV2 =
            [
                new CardV2Wrapper
                {
                    CardId = _cardId,
                    Card   = new CardV2
                    {
                        Header   = new CardHeader { Title = _title, Subtitle = _subtitle },
                        Sections = [new CardSection { Widgets = widgets }]
                    }
                }
            ]
        };
    }
}
