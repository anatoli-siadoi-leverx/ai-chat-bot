using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Infrastructure.OpenAi;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace Infrastructure.Google;

/// <summary>
/// Reads text from any Google Drive file by dispatching on MIME type.
///
/// <list type="bullet">
///   <item><c>text/*</c> — direct UTF-8 download</item>
///   <item><c>application/vnd.google-apps.document</c> — Drive export as <c>text/plain</c></item>
///   <item><c>image/*</c> — download raw bytes, send to OpenAI Vision API for OCR</item>
///   <item><c>application/vnd.openxmlformats-officedocument.wordprocessingml.document</c> (.docx)
///         — extract text from <c>word/document.xml</c>; also OCR any embedded images via Vision API</item>
///   <item>anything else — returns empty string</item>
/// </list>
/// </summary>
public sealed class DriveFileReader : IDriveFileReader
{
    private const string GoogleDocMime = "application/vnd.google-apps.document";
    private const string DocxMime = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    // Word XML namespace
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private readonly DriveService _drive;
    private readonly ChatClient _visionClient;
    private readonly ILogger<DriveFileReader> _logger;

    public DriveFileReader(
        IOptions<GoogleCredentialOptions> googleOptions,
        IOptions<OpenAiOptions> openAiOptions,
        ILogger<DriveFileReader> logger)
    {
        _logger = logger;

        var credential = GoogleCredential
            .FromFile(googleOptions.Value.ServiceAccountJson)
            .CreateScoped(DriveService.Scope.Drive);

        _drive = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "AiChatBot-FileReader"
        });

        _visionClient = new OpenAIClient(openAiOptions.Value.ApiKey).GetChatClient(openAiOptions.Value.Model);
    }

    /// <inheritdoc/>
    public async Task<string> ReadAsync(
        string fileId, string mimeType, string fileName,
        CancellationToken ct = default)
    {
        try
        {
            if (mimeType == GoogleDocMime)
            {
                return await ExportAsTextAsync(fileId, ct);
            }

            if (mimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            {
                return await DownloadAsTextAsync(fileId, ct);
            }

            if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return await ExtractTextFromImageAsync(fileId, mimeType, fileName, ct);
            }

            if (mimeType == DocxMime)
            {
                return await ExtractTextFromDocxAsync(fileId, fileName, ct);
            }

            _logger.LogInformation("File {FileId} ({MimeType}) — unsupported format, skipping", fileId, mimeType);

            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read content of Drive file {FileId}", fileId);

            return string.Empty;
        }
    }

    private async Task<string> ExportAsTextAsync(string fileId, CancellationToken ct)
    {
        var request = _drive.Files.Export(fileId, "text/plain");
        using var stream = new MemoryStream();

        await request.DownloadAsync(stream, ct);

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private async Task<string> DownloadAsTextAsync(string fileId, CancellationToken ct)
    {
        var request = _drive.Files.Get(fileId);
        using var stream = new MemoryStream();

        await request.DownloadAsync(stream, ct);

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private async Task<string> ExtractTextFromImageAsync(
        string fileId, string mimeType, string fileName, CancellationToken ct)
    {
        _logger.LogInformation("Downloading image {FileId} for Vision OCR", fileId);

        var request = _drive.Files.Get(fileId);
        using var stream = new MemoryStream();

        await request.DownloadAsync(stream, ct);

        var imageBytes = stream.ToArray();

        if (imageBytes.Length == 0)
        {
            _logger.LogWarning("Image {FileId} downloaded as empty; skipping Vision OCR", fileId);

            return string.Empty;
        }

        return await OcrImageBytesAsync(imageBytes, mimeType, fileName, ct);
    }

    // ── .docx handling ────────────────────────────────────────────────────────

    /// <summary>
    /// Downloads a .docx, extracts body text from <c>word/document.xml</c>,
    /// then OCRs any embedded images found in <c>word/media/</c>.
    /// </summary>
    private async Task<string> ExtractTextFromDocxAsync(
        string fileId, string fileName, CancellationToken ct)
    {
        _logger.LogInformation("Downloading .docx {FileId} ({Name}) for text extraction", fileId, fileName);

        var request = _drive.Files.Get(fileId);
        using var ms = new MemoryStream();

        await request.DownloadAsync(ms, ct);

        var bytes = ms.ToArray();

        if (bytes.Length == 0)
        {
            _logger.LogWarning(".docx {FileId} downloaded as empty", fileId);

            return string.Empty;
        }

        // 1. Extract paragraph text from word/document.xml.
        var bodyText = ParseDocxBodyText(bytes, fileId);

        // 2. OCR any embedded images (screenshots, terminal output, diagrams).
        var imageTexts = await OcrDocxImagesAsync(bytes, fileName, fileId, ct);

        if (imageTexts.Count == 0)
        {
            return bodyText;
        }

        var sb = new StringBuilder(bodyText);

        if (!string.IsNullOrWhiteSpace(bodyText))
        {
            sb.AppendLine();
        }

        sb.AppendLine("[Embedded image content:]");

        foreach (var t in imageTexts)
        {
            sb.AppendLine(t);
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Opens the .docx ZIP and returns all paragraph text joined by newlines.
    /// Uses only BCL: <c>System.IO.Compression</c> + <c>System.Xml.Linq</c>.
    /// </summary>
    private string ParseDocxBodyText(byte[] bytes, string fileId)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            var docEntry = zip.GetEntry("word/document.xml");

            if (docEntry is null)
            {
                _logger.LogWarning(".docx {FileId} contains no word/document.xml", fileId);

                return string.Empty;
            }

            using var docStream = docEntry.Open();
            var xdoc = XDocument.Load(docStream);

            // Each <w:p> is a paragraph; <w:t> elements inside hold the text runs.
            var sb = new StringBuilder();

            foreach (var para in xdoc.Descendants(W + "p"))
            {
                var paraText = string.Concat(para.Descendants(W + "t").Select(t => (string?)t ?? ""));

                if (!string.IsNullOrWhiteSpace(paraText))
                {
                    sb.AppendLine(paraText.Trim());
                }
            }

            var result = sb.ToString().Trim();
            _logger.LogInformation(".docx {FileId} body text extracted: {Chars} chars", fileId, result.Length);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse body text from .docx {FileId}", fileId);

            return string.Empty;
        }
    }

    /// <summary>
    /// Finds all images in <c>word/media/</c> of the .docx ZIP and OCRs each one.
    /// </summary>
    private async Task<List<string>> OcrDocxImagesAsync(
        byte[] bytes, string fileName, string fileId, CancellationToken ct)
    {
        var results = new List<string>();

        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            var mediaEntries = zip.Entries
                .Where(e => e.FullName.StartsWith("word/media/", StringComparison.OrdinalIgnoreCase)).ToList();

            if (mediaEntries.Count == 0)
            {
                return results;
            }

            _logger.LogInformation(
                ".docx {FileId} has {Count} embedded image(s) — starting Vision OCR",
                fileId, mediaEntries.Count);

            foreach (var entry in mediaEntries)
            {
                var imgMime = ExtensionToMime(Path.GetExtension(entry.Name));

                if (imgMime is null)
                {
                    continue;
                }

                using var imgMs = new MemoryStream();
                using var entryStream = entry.Open();

                await entryStream.CopyToAsync(imgMs, ct);

                var imgBytes = imgMs.ToArray();

                if (imgBytes.Length == 0)
                {
                    continue;
                }

                var text = await OcrImageBytesAsync(imgBytes, imgMime, fileName, ct);

                if (!string.IsNullOrWhiteSpace(text))
                {
                    results.Add(text.Trim());
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to OCR embedded images in .docx {FileId}", fileId);
        }
        return results;
    }

    private async Task<string> OcrImageBytesAsync(
        byte[] imageBytes, string mimeType, string contextName, CancellationToken ct)
    {
        _logger.LogInformation(
            "Sending {Bytes} bytes ({Mime}) to Vision API for '{Name}'",
            imageBytes.Length, mimeType, contextName);

        List<ChatMessage> messages =
        [
            new UserChatMessage(
                ChatMessageContentPart.CreateTextPart(
                    $"This image is from a bug report or log file named '{contextName}'. " +
                    "Extract all visible text from the image. " +
                    "Return only the extracted text, preserving line breaks where possible. " +
                    "If there is no text, return an empty string."),
                ChatMessageContentPart.CreateImagePart(
                    BinaryData.FromBytes(imageBytes), mimeType))
        ];

        var completion = await _visionClient.CompleteChatAsync(messages, cancellationToken: ct);

        var text = completion.Value.Content.FirstOrDefault()?.Text ?? string.Empty;

        _logger.LogInformation("Vision OCR extracted {Chars} chars from '{Name}'", text.Length, contextName);

        return text;
    }

    private static string? ExtensionToMime(string extension) =>
        extension.TrimStart('.').ToLowerInvariant() switch
        {
            "png" => "image/png",
            "jpg" or "jpeg" => "image/jpeg",
            "gif" => "image/gif",
            "bmp" => "image/bmp",
            "webp" => "image/webp",
            "tiff" or "tif" => "image/tiff",
            _ => null
        };
}
