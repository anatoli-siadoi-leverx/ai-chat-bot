namespace Infrastructure.Google;

/// <summary>
/// A file found in a Google Drive folder.
/// </summary>
/// <param name="Id">Google Drive file ID.</param>
/// <param name="Name">Display name (filename with extension).</param>
/// <param name="MimeType">MIME type reported by Drive (e.g. "text/plain", "application/vnd.google-apps.document").</param>
public sealed record DriveFile(string Id, string Name, string MimeType);

/// <summary>
/// Abstracts Google Drive operations needed by the Drive Watcher.
/// </summary>
public interface IGoogleDriveService
{
    /// <summary>Lists all non-trashed files directly inside <paramref name="folderId"/>.</summary>
    Task<IReadOnlyList<DriveFile>> ListFilesAsync(string folderId, CancellationToken ct = default);

    /// <summary>
    /// Reads the text content of a Drive file.
    /// Google Docs are exported as plain text; binary files return an empty string.
    /// </summary>
    Task<string> ReadTextContentAsync(string fileId, string mimeType, CancellationToken ct = default);

    /// <summary>
    /// Moves <paramref name="fileId"/> from <paramref name="fromFolderId"/> to <paramref name="toFolderId"/>
    /// by updating the file's parent collection.
    /// </summary>
    Task MoveFileAsync(string fileId, string fromFolderId, string toFolderId, CancellationToken ct = default);
}
