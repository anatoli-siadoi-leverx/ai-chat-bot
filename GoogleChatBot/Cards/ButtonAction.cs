namespace GoogleChatBot.Cards;

/// <summary>
/// Describes a single action button to be rendered on a Google Chat card.
/// </summary>
/// <param name="Label">Text displayed on the button.</param>
/// <param name="FunctionName">
/// Value sent back in <c>action.actionMethodName</c> when the button is clicked.
/// </param>
/// <param name="Parameters">Key-value pairs attached to the action payload.</param>
public sealed record ButtonAction(
    string Label,
    string FunctionName,
    IReadOnlyDictionary<string, string> Parameters);
