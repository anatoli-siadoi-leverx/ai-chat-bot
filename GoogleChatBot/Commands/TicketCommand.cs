using Domain.Repositories;
using Domain.Tickets;
using Domain.Workflow;
using GoogleChatBot.Cards;
using GoogleChatBot.Models.Incoming;
using GoogleChatBot.Models.Outgoing;
using Infrastructure.Google;

namespace GoogleChatBot.Commands;

/// <summary>
/// /ticket — creates a new <see cref="ErrorTicket"/> (state = New) and opens a Chat thread for it.
/// Accepts:
/// <list type="bullet">
///   <item>Plain-text: <c>/ticket NullReferenceException in PaymentService</c></item>
///   <item>Drive file attachment — content extracted via <see cref="IDriveFileReader"/>.</item>
///   <item>Combination: <c>/ticket</c> + attached file (caption is optional extra context).</item>
/// </list>
/// After creating the ticket the command posts a notification card + a content card to a new Chat
/// thread, then saves SpaceName/ThreadName on the ticket so pipeline results can reply there.
/// </summary>
public sealed class TicketCommand : ICommand
{
    private readonly ITicketRepository _repo;
    private readonly TicketWorkflow _workflow;
    private readonly IDriveFileReader _fileReader;
    private readonly IGoogleChatApiService _chatApi;
    private readonly ILogger<TicketCommand> _logger;

    public string Name => "ticket";
    public string Description =>
        "Creates a new error ticket. Usage: `/ticket <description>` or attach a Drive file.";

    public TicketCommand(
        ITicketRepository repo,
        TicketWorkflow workflow,
        IDriveFileReader fileReader,
        IGoogleChatApiService chatApi,
        ILogger<TicketCommand> logger)
    {
        _repo = repo;
        _workflow = workflow;
        _fileReader = fileReader;
        _chatApi = chatApi;
        _logger = logger;
    }

    public bool CanHandle(string input)
        => input.StartsWith("/ticket", StringComparison.OrdinalIgnoreCase);

    public async Task<BotResponse> ExecuteAsync(ChatMessage message)
    {
        var text = message.Text?.Trim() ?? string.Empty;
        var extraText = text["/ticket".Length..].Trim(); // optional caption after "/ticket"
        var attachment = (message.Attachments ?? []).FirstOrDefault();

        string title, description, source;
        string? sourceFileId = null;

        if (attachment is not null)
        {
            // ── Case 1: Drive file attached ───────────────────────────────────
            if (attachment.DriveDataRef?.DriveFileId is { Length: > 0 } driveFileId)
            {
                var content = await _fileReader.ReadAsync(
                    driveFileId,
                    attachment.ContentType,
                    attachment.ContentName);

                title = attachment.ContentName;
                description = !string.IsNullOrWhiteSpace(content) ? content
                             : !string.IsNullOrWhiteSpace(extraText) ? extraText
                             : attachment.ContentName;
                source = "GoogleDrive";
                sourceFileId = driveFileId;
            }
            // ── Case 2: Non-Drive uploaded file ───────────────────────────────
            else
            {
                title = attachment.ContentName;
                description = !string.IsNullOrWhiteSpace(extraText)
                    ? extraText
                    : $"Bug report from uploaded file: {attachment.ContentName}";
                source = "Manual";
            }
        }
        else
        {
            // ── Case 3: Plain text only ───────────────────────────────────────
            if (string.IsNullOrWhiteSpace(extraText))
            {
                return BotResponse.FromText(
                    "Usage: `/ticket <description>` — or attach a Drive file with the bug report.");
            }

            title = extraText.Length > 80 ? extraText[..80] : extraText;
            description = extraText;
            source = "Manual";
        }

        var ticket = new ErrorTicket
        {
            Title = title.Length > 80 ? title[..80] : title,
            Description = description,
            Source = source,
            SourceFileId = sourceFileId,
        };

        await _repo.AddAsync(ticket);

        // ── Open a Chat thread so the pipeline can post results there ─────────
        // Extract "spaces/XXXXX" from message resource name "spaces/XXXXX/messages/YYY".
        var spaceName = ExtractSpaceName(message.Name);

        if (!string.IsNullOrEmpty(spaceName))
        {
            try
            {
                // Notification card (header only) — starts the thread.
                var post = await _chatApi.PostNotificationCardAsync(
                    spaceName, ticket.Title,
                    $"Manually created ticket — {ticket.Source}");

                // Content card in that thread (description + Analyze button).
                await _chatApi.PostFileContentCardAsync(
                    spaceName, post.ThreadName,
                    ticket.Title, ticket.Description, ticket.Id);

                ticket.SpaceName = spaceName;
                ticket.ThreadName = post.ThreadName;
                ticket.MessageName = post.MessageName;

                await _repo.UpdateAsync(ticket);

                _logger.LogInformation("Ticket {Id} created with thread {Thread}", ticket.Id, ticket.ThreadName);
            }
            catch (Exception ex)
            {
                // Non-fatal: ticket exists but thread replies won't be available.
                _logger.LogWarning(ex, "Failed to open Chat thread for ticket {Id}", ticket.Id);
            }
        }
        else
        {
            _logger.LogWarning("Could not extract space name from message '{Name}' — ticket {Id} has no thread", message.Name, ticket.Id);
        }

        return BotResponse.FromCard(TicketCardBuilder.Build(ticket, _workflow));
    }

    /// <summary>"spaces/XXXXX/messages/YYY" → "spaces/XXXXX"</summary>
    private static string ExtractSpaceName(string messageName)
    {
        if (string.IsNullOrEmpty(messageName))
        {
            return string.Empty;
        }

        var parts = messageName.Split('/');

        return parts.Length >= 2 ? $"{parts[0]}/{parts[1]}" : string.Empty;
    }
}
