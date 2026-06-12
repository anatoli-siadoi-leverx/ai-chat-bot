using Domain.Repositories;
using Domain.Tickets;
using Infrastructure.Google;
using Infrastructure.OpenAi;
using Microsoft.Extensions.Options;

namespace Worker;

/// <summary>
/// Background service that polls the Google Drive <c>New/</c> folder on a configurable interval.
/// <para>
/// For each new file found it:
/// <list type="number">
///   <item>Reads the file content via <see cref="IDriveFileReader"/> (text, Google Docs, or Vision OCR for images).</item>
///   <item>Generates a one-sentence description using the LLM.</item>
///   <item>Creates an <see cref="ErrorTicket"/> with state <see cref="TicketState.New"/>.</item>
///   <item>Posts a notification card (header only) to the configured Google Chat space.</item>
///   <item>Posts a thread card with the file content and an Analyze button.</item>
///   <item>Saves <c>MessageName</c> and <c>ThreadName</c> on the ticket.</item>
///   <item>Moves the file from <c>New/</c> to <c>InProcess/</c>.</item>
/// </list>
/// </para>
/// The ticket stays in state <see cref="TicketState.New"/> until the user clicks the
/// <b>Analyze</b> button in Google Chat.
/// </summary>
public sealed class GoogleDriveWatcherWorker : BackgroundService
{
    private readonly IGoogleDriveService               _drive;
    private readonly IDriveFileReader                  _fileReader;
    private readonly IGoogleChatApiService             _chat;
    private readonly ILlmService                       _llm;
    private readonly ITicketRepository                 _tickets;
    private readonly GoogleCredentialOptions           _options;
    private readonly ILogger<GoogleDriveWatcherWorker> _logger;

    public GoogleDriveWatcherWorker(
        IGoogleDriveService               drive,
        IDriveFileReader                  fileReader,
        IGoogleChatApiService             chat,
        ILlmService                       llm,
        ITicketRepository                 tickets,
        IOptions<GoogleCredentialOptions> options,
        ILogger<GoogleDriveWatcherWorker> logger)
    {
        _drive      = drive;
        _fileReader = fileReader;
        _chat       = chat;
        _llm        = llm;
        _tickets    = tickets;
        _options    = options.Value;
        _logger     = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Drive watcher started — polling '{Folder}' every {Seconds}s",
            _options.NewFolderId,
            _options.PollingIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            await PollAsync(stoppingToken);
            await Task.Delay(
                TimeSpan.FromSeconds(_options.PollingIntervalSeconds),
                stoppingToken);
        }
    }

    // ── Poll ──────────────────────────────────────────────────────────────────

    private async Task PollAsync(CancellationToken ct)
    {
        IReadOnlyList<DriveFile> files;
        try
        {
            files = await _drive.ListFilesAsync(_options.NewFolderId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list files in Drive folder {FolderId}", _options.NewFolderId);
            return;
        }

        if (files.Count == 0)
        {
            _logger.LogDebug("No new files in Drive folder {FolderId}", _options.NewFolderId);
            return;
        }

        _logger.LogInformation("Found {Count} new file(s) in Drive folder", files.Count);

        foreach (var file in files)
        {
            try
            {
                await ProcessFileAsync(file, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Drive file {FileId} ({Name})", file.Id, file.Name);
            }
        }
    }

    // ── Process a single file ─────────────────────────────────────────────────

    private async Task ProcessFileAsync(DriveFile file, CancellationToken ct)
    {
        _logger.LogInformation("Processing Drive file {FileId} ({Name})", file.Id, file.Name);

        // 1. Read file content (text, Google Doc, or image OCR via Vision API)
        var content = await _fileReader.ReadAsync(file.Id, file.MimeType, file.Name, ct);

        // 2. Generate a short description via LLM
        var description = await GenerateDescriptionAsync(file.Name, content, ct);

        // 3. Create the ticket
        var ticket = new ErrorTicket
        {
            Title        = file.Name,
            Description  = content,
            Source       = "GoogleDrive",
            SourceFileId = file.Id,
            State        = TicketState.New
        };
        await _tickets.AddAsync(ticket);

        // 4. Post notification card to Google Chat (header only — no button)
        var post = await _chat.PostNotificationCardAsync(
            _options.ChatSpaceName, file.Name, description, ct);

        // 5. Post thread card with file content + Analyze button
        await _chat.PostFileContentCardAsync(
            _options.ChatSpaceName, post.ThreadName,
            file.Name, content, ticket.Id, ct);

        // 6. Save Chat message/thread names on the ticket
        ticket.MessageName = post.MessageName;
        ticket.ThreadName  = post.ThreadName;
        ticket.SpaceName   = _options.ChatSpaceName;
        await _tickets.UpdateAsync(ticket);

        // 7. Move file from New/ to InProcess/
        await _drive.MoveFileAsync(
            file.Id, _options.NewFolderId, _options.InProcessFolderId, ct);

        _logger.LogInformation(
            "Ticket {TicketId} created for file {FileId}; file moved to InProcess",
            ticket.Id, file.Id);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<string> GenerateDescriptionAsync(
        string fileName, string content, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(content))
            return $"New bug report file: {fileName}";

        try
        {
            var prompt =
                $"Summarize the following bug report in one sentence (max 120 characters). " +
                $"Reply with the summary only.\n\n---\n{TruncateForLlm(content)}";

            return await _llm.CompleteAsync(prompt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM description generation failed for {File}", fileName);
            return $"New bug report file: {fileName}";
        }
    }

    /// <summary>Truncates content to a safe length for the LLM prompt.</summary>
    private static string TruncateForLlm(string text, int maxChars = 3000) =>
        text.Length <= maxChars ? text : text[..maxChars] + "\n...(truncated)";
}
