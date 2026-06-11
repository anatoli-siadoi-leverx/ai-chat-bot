namespace Domain.Tickets;

public sealed class ErrorTicket
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Short human-readable title, e.g. "NullReferenceException in PaymentService".</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Full error description or stack trace.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Origin system: "GoogleSheets" | "GoogleDrive" | "Manual".</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Google Drive file ID where the error was found.</summary>
    public string? SourceFileId { get; set; }

    /// <summary>Google Sheets cell range that contained the error, e.g. "Sheet1!A2:E2".</summary>
    public string? SourceRange { get; set; }

    /// <summary>Google Chat space name ("spaces/xxxxx") where notifications are sent.</summary>
    public string? SpaceName { get; set; }

    /// <summary>Google Chat message name for reply threading ("spaces/xxx/messages/yyy").</summary>
    public string? MessageName { get; set; }

    /// <summary>Google Chat thread name for all subsequent replies ("spaces/xxx/threads/zzz").</summary>
    public string? ThreadName { get; set; }

    public TicketState State { get; set; } = TicketState.New;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>LLM-generated analysis written after the Analyzing stage.</summary>
    public string? AnalysisResult { get; set; }

    /// <summary>Git branch name created for the fix, e.g. "fix/ticket-abc123".</summary>
    public string? BranchName { get; set; }

    /// <summary>URL of the Pull Request opened by the fix pipeline.</summary>
    public string? PullRequestUrl { get; set; }
}
