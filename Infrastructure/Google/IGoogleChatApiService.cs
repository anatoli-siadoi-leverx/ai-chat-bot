namespace Infrastructure.Google;

/// <summary>
/// Result of a proactive Google Chat API message post.
/// </summary>
public sealed record ChatPostResult(string MessageName, string ThreadName);

/// <summary>
/// Proactive Google Chat API messaging — used by the Worker to send notifications
/// without an incoming webhook trigger.
/// </summary>
public interface IGoogleChatApiService
{
    /// <summary>
    /// Posts a notification card to the configured Chat space announcing a new bug-report file.
    /// Returns the names of the created message and its thread.
    /// </summary>
    Task<ChatPostResult> PostNotificationCardAsync(
        string spaceName, string fileName, string description,
        CancellationToken ct = default);

    /// <summary>
    /// Posts a card inside an existing thread containing the full file content,
    /// an optional thumbnail image, an optional "View in Drive" link, and an Analyze button.
    /// </summary>
    /// <param name="spaceName">Target space, e.g. "spaces/XXXXXXXXX".</param>
    /// <param name="threadName">Thread to reply to.</param>
    /// <param name="fileName">Display name of the Drive file.</param>
    /// <param name="content">Extracted text content of the file.</param>
    /// <param name="ticketId">Ticket GUID for the Analyze button parameter.</param>
    /// <param name="driveWebViewLink">Optional Drive UI URL — shown as a "View in Drive" button.</param>
    /// <param name="driveThumbnailLink">Optional Drive thumbnail URL — shown as an image widget above the text.</param>
    /// <param name="ct">Cancellation token.</param>
    Task PostFileContentCardAsync(
        string spaceName, string threadName,
        string fileName, string content, Guid ticketId,
        string? driveWebViewLink   = null,
        string? driveThumbnailLink = null,
        CancellationToken ct = default);

    /// <summary>Posts a plain-text reply inside an existing thread.</summary>
    Task PostThreadReplyAsync(
        string spaceName, string threadName, string text,
        CancellationToken ct = default);

    /// <summary>
    /// Posts an arbitrary message body as a thread reply (e.g. a CardResponse).
    /// </summary>
    Task PostThreadMessageAsync(
        string spaceName, string threadName, object body,
        CancellationToken ct = default);
}
