using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GDriveFile = global::Google.Apis.Drive.v3.Data.File;

namespace Infrastructure.Google;

/// <summary>
/// Google Drive operations backed by the official <c>Google.Apis.Drive.v3</c> client library.
/// Uses a service-account credential loaded from <see cref="GoogleCredentialOptions.ServiceAccountJson"/>.
/// </summary>
public sealed class GoogleDriveService : IGoogleDriveService
{
    private readonly DriveService                    _drive;
    private readonly ILogger<GoogleDriveService>     _logger;

    // MIME types that Drive uses for native Google documents
    private const string GoogleDocMime  = "application/vnd.google-apps.document";
    private const string PlainTextMime  = "text/plain";

    public GoogleDriveService(
        IOptions<GoogleCredentialOptions> options,
        ILogger<GoogleDriveService>       logger)
    {
        _logger = logger;
        var credential = GoogleCredential.FromFile(options.Value.ServiceAccountJson).CreateScoped(DriveService.Scope.Drive);
        _drive = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName       = "AiChatBot-DriveWatcher"
        });
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DriveFile>> ListFilesAsync(
        string folderId, CancellationToken ct = default)
    {
        var request = _drive.Files.List();
        request.Q      = $"'{folderId}' in parents and trashed = false";
        request.Fields = "files(id,name,mimeType)";

        var response = await request.ExecuteAsync(ct);

        return (response.Files ?? [])
            .Select(f => new DriveFile(f.Id!, f.Name!, f.MimeType!))
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<string> ReadTextContentAsync(
        string fileId, string mimeType, CancellationToken ct = default)
    {
        try
        {
            if (mimeType == GoogleDocMime)
            {
                // Export Google Doc as plain text
                var exportRequest = _drive.Files.Export(fileId, PlainTextMime);
                using var stream  = new MemoryStream();
                await exportRequest.DownloadAsync(stream, ct);
                return System.Text.Encoding.UTF8.GetString(stream.ToArray());
            }

            if (mimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            {
                // Direct download for plain-text files
                var getRequest    = _drive.Files.Get(fileId);
                using var stream  = new MemoryStream();
                await getRequest.DownloadAsync(stream, ct);
                return System.Text.Encoding.UTF8.GetString(stream.ToArray());
            }

            // Images and other binary formats: Stage 10 adds Vision API support.
            _logger.LogInformation(
                "File {FileId} has unsupported MIME type {MimeType}; text extraction skipped",
                fileId, mimeType);
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read content of Drive file {FileId}", fileId);
            return string.Empty;
        }
    }

    /// <inheritdoc/>
    public async Task MoveFileAsync(
        string fileId, string fromFolderId, string toFolderId, CancellationToken ct = default)
    {
        var update         = _drive.Files.Update(new GDriveFile(), fileId);
        update.AddParents    = toFolderId;
        update.RemoveParents = fromFolderId;
        update.Fields        = "id,parents";
        await update.ExecuteAsync(ct);

        _logger.LogInformation(
            "Moved Drive file {FileId} from {From} to {To}",
            fileId, fromFolderId, toFolderId);
    }
}
