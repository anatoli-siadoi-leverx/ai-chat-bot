using GoogleChatBot.Models.Incoming;
using GoogleChatBot.Models.Outgoing;

namespace GoogleChatBot.Handlers;

/// <summary>
/// Handles individual Google Chat event types and returns a <see cref="BotResponse"/>.
/// <see cref="GoogleChatBot.Controllers.ChatController"/> routes events here
/// and converts the result to the correct wire format.
/// </summary>
public interface IChatEventHandler
{
    BotResponse OnAddedToSpace(ChatEvent chatEvent);
    Task<BotResponse> OnMessageAsync(ChatEvent chatEvent);
    Task<BotResponse> OnCardClickedAsync(ChatEvent chatEvent);
}
