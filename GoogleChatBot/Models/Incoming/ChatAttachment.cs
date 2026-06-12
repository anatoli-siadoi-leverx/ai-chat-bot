using System.Text.Json.Serialization;

namespace GoogleChatBot.Models.Incoming;

/// <summary>
/// A file attached to a Google Chat message.
/// Can be a Drive file (<see cref="DriveDataRef"/> set) or
/// an uploaded file (<see cref="AttachmentDataRef"/> set).
/// Docs: https://developers.google.com/chat/api/reference/rest/v1/spaces.messages.attachments
/// </summary>
public sealed class ChatAttachment
{
    /// <summary>Resource name, e.g. "spaces/.../messages/.../attachments/...".</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Original filename as shown to the user.</summary>
    [JsonPropertyName("contentName")]
    public string ContentName { get; set; } = string.Empty;

    /// <summary>MIME type of the file.</summary>
    [JsonPropertyName("contentType")]
    public string ContentType { get; set; } = string.Empty;

    /// <summary>"DRIVE_FILE" | "UPLOADED_CONTENT"</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    /// <summary>Non-null when <see cref="Source"/> is "DRIVE_FILE".</summary>
    [JsonPropertyName("driveDataRef")]
    public ChatDriveDataRef? DriveDataRef { get; set; }

    /// <summary>Non-null when <see cref="Source"/> is "UPLOADED_CONTENT".</summary>
    [JsonPropertyName("attachmentDataRef")]
    public ChatAttachmentDataRef? AttachmentDataRef { get; set; }

    /// <summary>Short-lived URL for downloading raw bytes (uploaded files only).</summary>
    [JsonPropertyName("downloadUri")]
    public string? DownloadUri { get; set; }

    /// <summary>Short-lived thumbnail preview URL.</summary>
    [JsonPropertyName("thumbnailUri")]
    public string? ThumbnailUri { get; set; }
}

/// <summary>Reference to a file stored in Google Drive.</summary>
public sealed class ChatDriveDataRef
{
    [JsonPropertyName("driveFileId")]
    public string DriveFileId { get; set; } = string.Empty;
}

/// <summary>Reference to a file uploaded directly to Google Chat.</summary>
public sealed class ChatAttachmentDataRef
{
    [JsonPropertyName("resourceName")]
    public string ResourceName { get; set; } = string.Empty;

    [JsonPropertyName("attachmentUploadToken")]
    public string? AttachmentUploadToken { get; set; }
}
