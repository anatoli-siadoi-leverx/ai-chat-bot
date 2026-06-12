using GoogleChatBot.Commands;
using GoogleChatBot.Controllers;
using GoogleChatBot.Models.Incoming;
using GoogleChatBot.Models.Outgoing;
using Infrastructure.OpenAi;

namespace GoogleChatBot.Handlers;

/// <summary>
/// Handles individual Google Chat event types.
/// Contains all business logic for ADDED_TO_SPACE, MESSAGE, and CARD_CLICKED events.
/// </summary>
public sealed class ChatEventHandler : IChatEventHandler
{
    private readonly CommandDispatcher _dispatcher;
    private readonly ILlmService _llm;
    private readonly ActionController _actionController;
    private readonly ILogger<ChatEventHandler> _logger;

    public ChatEventHandler(
        CommandDispatcher dispatcher,
        ILlmService llm,
        ActionController actionController,
        ILogger<ChatEventHandler> logger)
    {
        _dispatcher = dispatcher;
        _llm = llm;
        _actionController = actionController;
        _logger = logger;
    }

    public BotResponse OnAddedToSpace(ChatEvent chatEvent)
    {
        var spaceLabel = chatEvent.Space?.Type == "DM" ? "a DM" : "this space";

        return BotResponse.FromText(
            $"Hello! I'm your AI assistant. I was just added to {spaceLabel}. " +
            "Type `/help` to see my commands, or just ask me anything.");
    }

    public async Task<BotResponse> OnMessageAsync(ChatEvent chatEvent)
    {
        var message = chatEvent.Message ?? new ChatMessage();
        var text    = message.Text?.Trim() ?? string.Empty;
        var sender  = message.Sender?.DisplayName ?? "Unknown";

        // 1. Slash-commands have priority.
        var commandResult = await _dispatcher.DispatchAsync(message);

        if (commandResult is not null)
        {
            return commandResult;
        }

        // 2. Empty message guard.
        if (string.IsNullOrEmpty(text))
        {
            return BotResponse.FromText($"Hi **{sender}**! How can I help you?");
        }

        // 3. Fallback: send to LLM.
        return BotResponse.FromText(await CallLlmAsync(text));
    }

    public async Task<BotResponse> OnCardClickedAsync(ChatEvent chatEvent)
    {
        if (chatEvent.Action is null)
        {
            _logger.LogWarning("CARD_CLICKED received with no action payload");

            return BotResponse.FromText("Card action received but no action data found.");
        }

        _logger.LogInformation("Card action: Function={Function}", chatEvent.Action.ActionMethodName);

        return await _actionController.HandleAsync(chatEvent.Action);
    }

    private async Task<string> CallLlmAsync(string text)
    {
        try
        {
            return await _llm.CompleteAsync(text);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM request failed");

            return "Sorry, I encountered an error while processing your message. Please try again.";
        }
    }
}
