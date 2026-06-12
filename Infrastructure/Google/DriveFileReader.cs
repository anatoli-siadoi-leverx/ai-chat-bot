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
///   <item>anything else — returns empty string</item>
/// </list>
///
/// This class creates its own <see cref="DriveService"/> instance (same pattern as
/// <see cref="GoogleDriveService"/>) to keep infrastructure components independent.
/// </summary>
public sealed class DriveFileReader : IDriveFileReader
{
    private const string GoogleDocMime = "application/vnd.google-apps.document";

    private readonly DriveService _drive;
    private readonly ChatClient _visionClient;
    private readonly ILogger<DriveFileReader> _logger;

    public DriveFileReader(
        IOptions<GoogleCredentialOptions> googleOptions,
        IOptions<OpenAiOptions> openAiOptions,
        ILogger<DriveFileReader> logger)
    {
        _logger = logger;

        // Drive credential (same service account as GoogleDriveService)
        var credential = GoogleCredential
            .FromFile(googleOptions.Value.ServiceAccountJson)
            .CreateScoped(DriveService.Scope.Drive);

        _drive = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "AiChatBot-FileReader"
        });

        // Vision client reuses the same model that is configured for the bot.
        // gpt-4o and gpt-4o-mini both support vision.
        _visionClient = new OpenAIClient(openAiOptions.Value.ApiKey)
            .GetChatClient(openAiOptions.Value.Model);
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

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private async Task<string> DownloadAsTextAsync(string fileId, CancellationToken ct)
    {
        var request = _drive.Files.Get(fileId);
        using var stream = new MemoryStream();

        await request.DownloadAsync(stream, ct);

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
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

        _logger.LogInformation(
            "Sending image {FileId} ({Bytes} bytes, {Mime}) to Vision API",
            fileId, imageBytes.Length, mimeType);

        List<ChatMessage> messages =
        [
            new UserChatMessage(
                ChatMessageContentPart.CreateTextPart(
                    $"This file is named '{fileName}' and contains a bug report or log screenshot. " +
                    "Extract all visible text from the image. " +
                    "Return only the extracted text, preserving line breaks where possible. " +
                    "If there is no text, return an empty string."),
                ChatMessageContentPart.CreateImagePart(
                    BinaryData.FromBytes(imageBytes), mimeType))
        ];

        var completion = await _visionClient.CompleteChatAsync(messages, cancellationToken: ct);

        var text = completion.Value.Content.FirstOrDefault()?.Text ?? string.Empty;

        _logger.LogInformation(
            "Vision OCR extracted {Chars} chars from {FileId}", text.Length, fileId);

        return text;
    }
}
