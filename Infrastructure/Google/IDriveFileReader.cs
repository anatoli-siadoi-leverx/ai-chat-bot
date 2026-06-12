namespace Infrastructure.Google;

/// <summary>
/// Reads the text content of a Google Drive file regardless of its format.
/// Dispatches internally by MIME type:
/// <list type="bullet">
///   <item><c>text/*</c> — direct download</item>
///   <item><c>application/vnd.google-apps.document</c> — Drive export as <c>text/plain</c></item>
///   <item><c>image/*</c> — binary download + OpenAI Vision API OCR</item>
///   <item>anything else — returns empty string</item>
/// </list>
/// </summary>
public interface IDriveFileReader
{
    /// <summary>
    /// Returns the extracted text for the given Drive file.
    /// Never throws — returns an empty string on failure or unsupported format.
    /// </summary>
    /// <param name="fileId">Google Drive file ID.</param>
    /// <param name="mimeType">MIME type reported by Drive.</param>
    /// <param name="fileName">Original filename — used as context in Vision prompts.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<string> ReadAsync(
        string fileId, string mimeType, string fileName,
        CancellationToken ct = default);
}
