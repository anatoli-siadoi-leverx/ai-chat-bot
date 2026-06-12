using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Infrastructure.Google;

/// <summary>
/// Calls the Google Chat REST API using a service-account credential.
/// </summary>
public sealed class GoogleChatApiService : IGoogleChatApiService
{
    private const string ChatApiBase = "https://chat.googleapis.com/v1";
    private const string ChatScope = "https://www.googleapis.com/auth/chat.bot";

    private readonly HttpClient _http;
    private readonly GoogleCredential _credential;
    private readonly ILogger<GoogleChatApiService> _logger;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public GoogleChatApiService(
        IOptions<GoogleCredentialOptions> options,
        ILogger<GoogleChatApiService> logger)
    {
        _logger = logger;
        _http = new HttpClient();
        _credential = GoogleCredential
            .FromFile(options.Value.ServiceAccountJson)
            .CreateScoped(ChatScope);
    }

    /// <inheritdoc/>
    public async Task<ChatPostResult> PostNotificationCardAsync(
        string spaceName, string fileName, string description,
        CancellationToken ct = default)
    {
        var body = new
        {
            cardsV2 = new[]
            {
                new
                {
                    cardId = "ticket-notification",
                    card = new
                    {
                        header = new
                        {
                            title = $"🐛 New bug report: {fileName}",
                            subtitle = description
                        }
                    }
                }
            }
        };

        var response = await PostAsync(spaceName, body, threadName: null, ct);
        return ParseChatPostResult(response);
    }

    /// <inheritdoc/>
    public async Task PostFileContentCardAsync(
        string spaceName, string threadName,
        string fileName, string content, Guid ticketId,
        string? driveWebViewLink   = null,
        string? driveThumbnailLink = null,
        CancellationToken ct = default)
    {
        var displayText = string.IsNullOrWhiteSpace(content)
            ? $"*File:* {fileName}\n_(Content not available)_"
            : $"*File:* {fileName}\n\n```\n{TruncateForChat(content)}\n```";

        var widgets = new List<object>();

        // Optional thumbnail — shown when Drive returns a preview image URL.
        if (!string.IsNullOrEmpty(driveThumbnailLink))
        {
            if (!string.IsNullOrEmpty(driveWebViewLink))
            {
                widgets.Add(new
                {
                    image = new
                    {
                        imageUrl = driveThumbnailLink,
                        altText = $"Preview: {fileName}",
                        onClick = new { openLink = new { url = driveWebViewLink } }
                    }
                });
            }
            else
            {
                widgets.Add(new
                {
                    image = new
                    {
                        imageUrl = driveThumbnailLink,
                        altText = $"Preview: {fileName}"
                    }
                });
            }
        }

        widgets.Add(new { textParagraph = new { text = displayText } });

        // Buttons: Analyze (always) + View in Drive (when link available).
        var buttons = new List<object>
        {
            new
            {
                text = "🔍 Analyze",
                onClick = new
                {
                    action = new
                    {
                        function = "analyze",
                        parameters = new[] { new { key = "ticketId", value = ticketId.ToString() } }
                    }
                }
            }
        };

        if (!string.IsNullOrEmpty(driveWebViewLink))
        {
            buttons.Add(new
            {
                text = "📁 View in Drive",
                onClick = new { openLink = new { url = driveWebViewLink } }
            });
        }

        widgets.Add(new { buttonList = new { buttons = buttons.ToArray() } });

        var body = new
        {
            cardsV2 = new[]
            {
                new
                {
                    cardId = "file-content",
                    card = new
                    {
                        sections = new[]
                        {
                            new { widgets = widgets.ToArray() }
                        }
                    }
                }
            }
        };

        await PostAsync(spaceName, body, threadName, ct);
    }

    /// <inheritdoc/>
    public async Task PostThreadReplyAsync(
        string spaceName, string threadName, string text,
        CancellationToken ct = default)
    {
        var body = new { text };

        await PostAsync(spaceName, body, threadName, ct);
    }

    /// <inheritdoc/>
    public Task PostThreadMessageAsync(
        string spaceName, string threadName, object body,
        CancellationToken ct = default)
        => PostAsync(spaceName, body, threadName, ct);

    private async Task<string> PostAsync(
        string spaceName, object body, string? threadName, CancellationToken ct)
    {
        var token = await _credential.UnderlyingCredential
            .GetAccessTokenForRequestAsync(cancellationToken: ct);

        object payload = threadName is not null
            ? MergeThread(body, threadName)
            : body;

        var json = JsonSerializer.Serialize(payload, _jsonOpts);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var url = threadName is not null
            ? $"{ChatApiBase}/{spaceName}/messages?messageReplyOption=REPLY_MESSAGE_FALLBACK_TO_NEW_THREAD"
            : $"{ChatApiBase}/{spaceName}/messages";

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _http.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Google Chat API error {Status}: {Body}",
                (int)response.StatusCode, responseBody);
            response.EnsureSuccessStatusCode();
        }

        _logger.LogDebug("Chat API response: {Body}", responseBody);

        return responseBody;
    }

    private static object MergeThread(object body, string threadName)
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(body, _jsonOpts))!.AsObject();
        node["thread"] = JsonNode.Parse(JsonSerializer.Serialize(new { name = threadName }));
        return node;
    }

    private static ChatPostResult ParseChatPostResult(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var msgName = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        var thdName = root.TryGetProperty("thread", out var t)
                     && t.TryGetProperty("name", out var tn) ? tn.GetString() ?? "" : "";

        return new ChatPostResult(msgName, thdName);
    }

    /// <summary>Truncates content to Google Chat's message size limit (~4 KB).</summary>
    private static string TruncateForChat(string text, int maxChars = 3500) =>
        text.Length <= maxChars ? text : text[..maxChars] + "\n...(truncated)";
}
