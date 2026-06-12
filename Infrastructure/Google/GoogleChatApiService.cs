using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Infrastructure.Google;

/// <summary>
/// Calls the Google Chat REST API using a service-account credential.
/// Messages are sent as HTTP POST requests with a Bearer token obtained from
/// <see cref="GoogleCredential"/>.
/// </summary>
public sealed class GoogleChatApiService : IGoogleChatApiService
{
    private const string ChatApiBase = "https://chat.googleapis.com/v1";
    private const string ChatScope   = "https://www.googleapis.com/auth/chat.bot";

    private readonly HttpClient                        _http;
    private readonly GoogleCredential                  _credential;
    private readonly ILogger<GoogleChatApiService>     _logger;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public GoogleChatApiService(
        IOptions<GoogleCredentialOptions> options,
        ILogger<GoogleChatApiService>     logger)
    {
        _logger = logger;
        _http = new HttpClient();
        _credential = GoogleCredential.FromFile(options.Value.ServiceAccountJson).CreateScoped(ChatScope);
    }

    /// <inheritdoc/>
    public async Task<ChatPostResult> PostNotificationCardAsync(
        string spaceName, string fileName, string description,
        CancellationToken ct = default)
    {
        // Notification card — header only, no buttons.
        // The Analyze button is in the file-content card posted as a thread reply.
        var body = new
        {
            cardsV2 = new[]
            {
                new
                {
                    cardId = "ticket-notification",
                    card   = new
                    {
                        header = new
                        {
                            title    = $"New bug report: {fileName}",
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
        CancellationToken ct = default)
    {
        var displayText = string.IsNullOrWhiteSpace(content)
            ? $"*File:* {fileName}\n_(Binary file — text extraction not available yet)_"
            : $"*File:* {fileName}\n\n```\n{TruncateForChat(content)}\n```";

        var body = new
        {
            cardsV2 = new[]
            {
                new
                {
                    cardId = "file-content",
                    card   = new
                    {
                        sections = new[]
                        {
                            new
                            {
                                widgets = new object[]
                                {
                                    new
                                    {
                                        textParagraph = new { text = displayText }
                                    },
                                    new
                                    {
                                        buttonList = new
                                        {
                                            buttons = new[]
                                            {
                                                new
                                                {
                                                    text    = "Analyze",
                                                    onClick = new
                                                    {
                                                        action = new
                                                        {
                                                            function   = "analyze",
                                                            parameters = new[]
                                                            {
                                                                new { key = "ticketId", value = ticketId.ToString() }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
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

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<string> PostAsync(
        string spaceName, object body, string? threadName, CancellationToken ct)
    {
        var token = await _credential.UnderlyingCredential
            .GetAccessTokenForRequestAsync(cancellationToken: ct);

        // If threadName is provided, inject it into the body and use reply option
        object payload = threadName is not null
            ? MergeThread(body, threadName)
            : body;

        var json    = JsonSerializer.Serialize(payload, _jsonOpts);
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
            response.EnsureSuccessStatusCode();   // throws HttpRequestException
        }

        _logger.LogDebug("Chat API response: {Body}", responseBody);
        return responseBody;
    }

    /// <summary>
    /// Merges a <c>thread</c> property into the body object by round-tripping through
    /// <see cref="JsonNode"/> so we don't need to declare a separate DTO type.
    /// </summary>
    private static object MergeThread(object body, string threadName)
    {
        var node   = JsonNode.Parse(JsonSerializer.Serialize(body, _jsonOpts))!.AsObject();
        node["thread"] = JsonNode.Parse(JsonSerializer.Serialize(new { name = threadName }));
        return node;
    }

    private static ChatPostResult ParseChatPostResult(string json)
    {
        using var doc    = JsonDocument.Parse(json);
        var root         = doc.RootElement;
        var messageName  = root.TryGetProperty("name",   out var n) ? n.GetString() ?? string.Empty : string.Empty;
        var threadName   = root.TryGetProperty("thread", out var t)
            && t.TryGetProperty("name", out var tn) ? tn.GetString() ?? string.Empty : string.Empty;
        return new ChatPostResult(messageName, threadName);
    }

    /// <summary>Truncates content to Google Chat's message size limit (~4 KB).</summary>
    private static string TruncateForChat(string text, int maxChars = 3500) =>
        text.Length <= maxChars ? text : text[..maxChars] + "\n...(truncated)";
}
