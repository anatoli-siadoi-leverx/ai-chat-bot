using GoogleChatBot.Models.Outgoing;
using Tools;

namespace GoogleChatBot.Commands;

/// <summary>
/// /time — delegates to <see cref="TimeTool"/>.
/// </summary>
public sealed class TimeCommand : ICommand
{
    private readonly TimeTool _tool;

    public string Name        => "time";
    public string Description => "Shows the current UTC date and time";

    public TimeCommand(TimeTool tool)
    {
        _tool = tool;
    }

    public bool CanHandle(string input) =>
        input.Equals("/time", StringComparison.OrdinalIgnoreCase) ||
        input.StartsWith("/time ", StringComparison.OrdinalIgnoreCase);

    public async Task<BotResponse> ExecuteAsync(string input)
        => BotResponse.FromText(await _tool.ExecuteAsync(input));
}
