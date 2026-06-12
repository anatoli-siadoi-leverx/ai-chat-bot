using GoogleChatBot.Cards;
using GoogleChatBot.Models.Incoming;
using GoogleChatBot.Models.Outgoing;
using Infrastructure.Google;
using Microsoft.Extensions.Options;

namespace GoogleChatBot.Commands;

/// <summary>
/// /report — counts files in the New / InProcess / Done Drive folders
/// and returns a coloured Bug Stats card.
/// </summary>
public sealed class ReportCommand(
    IGoogleDriveService drive,
    IOptions<GoogleCredentialOptions> opts) : ICommand
{
    public string Name => "report";
    public string Description => "Shows file counts across New / In-Process / Done Drive folders.";

    public bool CanHandle(string input)
        => input.Equals("/report", StringComparison.OrdinalIgnoreCase);

    public async Task<BotResponse> ExecuteAsync(ChatMessage message)
    {
        var o = opts.Value;

        var t1 = SafeCountAsync(o.NewFolderId);
        var t2 = SafeCountAsync(o.InProcessFolderId);
        var t3 = SafeCountAsync(o.DoneFolderId);
        await Task.WhenAll(t1, t2, t3);

        var newCount = t1.Result;
        var inProcessCount = t2.Result;
        var doneCount = t3.Result;

        var active = newCount + inProcessCount;
        var resolved = doneCount;
        var total = active + resolved;

        var statsHtml =
            $"<font color=\"#F9AB00\"><b>●</b></font>  <b>NEW</b>         <b>{FormatCount(newCount)}</b><br>" +
            $"<font color=\"#EA4335\"><b>●</b></font>  <b>IN PROCESS</b>  <b>{FormatCount(inProcessCount)}</b><br>" +
            $"<font color=\"#34A853\"><b>●</b></font>  <b>DONE</b>        <b>{FormatCount(doneCount)}</b>";

        var summaryHtml =
            $"Active: <b>{active}</b>  ·  " +
            $"Resolved: <b>{resolved}</b>  ·  " +
            $"Total: <b>{total}</b>";

        var card = new CardBuilder()
            .WithCardId("bug-stats")
            .WithTitle("🐛 Bug Stats")
            .WithSubtitle($"as of {DateTimeOffset.UtcNow:dd MMM yyyy HH:mm} UTC")
            .AddParagraph(statsHtml)
            .AddParagraph(summaryHtml)
            .Build();

        return BotResponse.FromCard(card);
    }

    private async Task<int> SafeCountAsync(string folderId)
    {
        if (string.IsNullOrWhiteSpace(folderId)) return 0;

        try
        {
            var files = await drive.ListFilesAsync(folderId);

            return files.Count;
        }
        catch
        {
            return -1;
        }
    }

    private static string FormatCount(int count) => count < 0 ? "?" : count.ToString();
}
