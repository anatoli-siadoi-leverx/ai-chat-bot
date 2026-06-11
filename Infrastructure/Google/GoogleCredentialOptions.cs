namespace Infrastructure.Google;

/// <summary>
/// Configuration for Google Drive folder watching and Chat API notifications.
/// Bound from the "Google" configuration section.
/// Store <see cref="ServiceAccountJson"/> in user-secrets or an environment variable.
/// </summary>
public sealed class GoogleCredentialOptions
{
    public const string SectionName = "Google";

    /// <summary>
    /// Full JSON content of the Google service-account key file.
    /// Store via: dotnet user-secrets set "Google:ServiceAccountJson" "{ ... }"
    /// </summary>
    public string ServiceAccountJson { get; set; } = string.Empty;

    // ── Drive folder IDs ──────────────────────────────────────────────────────

    /// <summary>Google Drive folder ID for incoming bug reports (watched for new files).</summary>
    public string NewFolderId { get; set; } = string.Empty;

    /// <summary>Google Drive folder ID where files are moved once a ticket is created.</summary>
    public string InProcessFolderId { get; set; } = string.Empty;

    /// <summary>Google Drive folder ID where files are moved once a ticket is closed.</summary>
    public string DoneFolderId { get; set; } = string.Empty;

    // ── Google Chat ───────────────────────────────────────────────────────────

    /// <summary>
    /// Google Chat space name where notifications are posted, e.g. "spaces/XXXXXXXXX".
    /// </summary>
    public string ChatSpaceName { get; set; } = string.Empty;

    // ── Polling ───────────────────────────────────────────────────────────────

    /// <summary>How often to poll the New folder, in seconds. Default: 300 (5 minutes).</summary>
    public int PollingIntervalSeconds { get; set; } = 300;
}
