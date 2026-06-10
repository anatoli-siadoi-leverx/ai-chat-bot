# AI Chat Bot — Implementation Plan

## Overview

Solution: `AiChatBotSolution.slnx`, target framework: **net10.0**

Projects:
| Project | Type | Role |
|---|---|---|
| `Domain` | Class library | Models, enums — zero dependencies |
| `Tools` | Class library | Tool interfaces and implementations |
| `Infrastructure` | Class library | OpenAI, GitHub, Google API clients |
| `GoogleChatBot` | ASP.NET Web API | Webhook receiver, response sender |
| `McpServer` | ASP.NET Web API | MCP protocol endpoint |
| `Worker` | BackgroundService | Periodic polling (Drive/Sheets) |

---

## Task A — Create IMPLEMENTATION_PLAN.md

Create `IMPLEMENTATION_PLAN.md` at the **root** of the repository containing the full 13-stage design document (see Stage Reference section below).

---

## Task B — Domain Models

Delete placeholder `Domain/Class1.cs`.

Create:

### `Domain/Tickets/TicketState.cs`
```csharp
namespace Domain.Tickets;

public enum TicketState
{
    New = 0,
    Analyzing = 1,
    Analyzed = 2,
    Fixing = 3,
    Fixed = 4,
    Failed = 5,
    Closed = 6
}
```

### `Domain/Tickets/ErrorTicket.cs`
```csharp
namespace Domain.Tickets;

public sealed class ErrorTicket
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;     // "GoogleSheets" | "GoogleDrive"
    public string? SourceFileId { get; set; }              // Google Drive file ID
    public string? SourceRange { get; set; }               // Sheets range, e.g. "Sheet1!A2:E2"
    public string? SpaceName { get; set; }                 // Google Chat space name
    public string? MessageName { get; set; }               // Google Chat message for reply threading
    public TicketState State { get; set; } = TicketState.New;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? AnalysisResult { get; set; }
    public string? BranchName { get; set; }
    public string? PullRequestUrl { get; set; }
}
```

---

## Task C — Stage 1: Base Google Chat Bot

### Goal
Accept Google Chat webhook, parse the message, return a plain-text echo/greeting.

### GoogleChatBot project changes

**Update `GoogleChatBot.csproj`** — add `Microsoft.AspNetCore.OpenApi` for Swagger (optional but useful):
No extra packages needed for Stage 1; controllers are included in the Web SDK.

**Update `Program.cs`**:
```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
var app = builder.Build();
app.MapControllers();
app.Run();
```

**Create `GoogleChatBot/Models/Incoming/ChatEvent.cs`**:
```csharp
namespace GoogleChatBot.Models.Incoming;

public sealed class ChatEvent
{
    public string Type { get; set; } = string.Empty;   // "MESSAGE" | "ADDED_TO_SPACE" | "REMOVED_FROM_SPACE"
    public ChatMessage? Message { get; set; }
    public ChatSpace? Space { get; set; }
}
```

**Create `GoogleChatBot/Models/Incoming/ChatMessage.cs`**:
```csharp
namespace GoogleChatBot.Models.Incoming;

public sealed class ChatMessage
{
    public string Name { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public ChatSender? Sender { get; set; }
}
```

**Create `GoogleChatBot/Models/Incoming/ChatSender.cs`**:
```csharp
namespace GoogleChatBot.Models.Incoming;

public sealed class ChatSender
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
```

**Create `GoogleChatBot/Models/Incoming/ChatSpace.cs`**:
```csharp
namespace GoogleChatBot.Models.Incoming;

public sealed class ChatSpace
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;   // "DM" | "ROOM"
}
```

**Create `GoogleChatBot/Models/Outgoing/ChatResponse.cs`**:
```csharp
namespace GoogleChatBot.Models.Outgoing;

public sealed class ChatResponse
{
    public string Text { get; set; } = string.Empty;

    public static ChatResponse From(string text) => new() { Text = text };
}
```

**Create `GoogleChatBot/Controllers/ChatController.cs`**:
```csharp
using GoogleChatBot.Models.Incoming;
using GoogleChatBot.Models.Outgoing;
using Microsoft.AspNetCore.Mvc;

namespace GoogleChatBot.Controllers;

[ApiController]
[Route("chat")]
public sealed class ChatController : ControllerBase
{
    private readonly ILogger<ChatController> _logger;

    public ChatController(ILogger<ChatController> logger)
    {
        _logger = logger;
    }

    [HttpPost]
    public IActionResult HandleEvent([FromBody] ChatEvent chatEvent)
    {
        _logger.LogInformation("Received event type={Type} from space={Space}",
            chatEvent.Type, chatEvent.Space?.Name);

        return chatEvent.Type switch
        {
            "ADDED_TO_SPACE" => Ok(ChatResponse.From("Hello! I'm your AI assistant. Type /help to see what I can do.")),
            "MESSAGE"        => Ok(HandleMessage(chatEvent)),
            _                => Ok(new { })
        };
    }

    private static ChatResponse HandleMessage(ChatEvent chatEvent)
    {
        var text = chatEvent.Message?.Text?.Trim() ?? string.Empty;
        var sender = chatEvent.Message?.Sender?.DisplayName ?? "Unknown";

        return ChatResponse.From($"Hi {sender}! You said: \"{text}\"");
    }
}
```

### Result
- `POST /chat` accepts Google Chat webhook JSON
- Responds with `{ "text": "..." }` for MESSAGE and ADDED_TO_SPACE events
- REMOVED_FROM_SPACE returns empty JSON `{}`

---

## Stage Reference — All 13 Stages

### Stage 1 — Base Bot (Google Chat Webhook)
**Goal:** Receive messages, return plain text.
**Implements:** ChatController, ChatEvent/Message/Sender/Space DTOs, ChatResponse.
**Result:** Working webhook endpoint, verifiable via curl or ngrok.

---

### Stage 2 — Command System
**Goal:** Parse and dispatch user commands like `/help`, `/status`, `/ticket`.
**Classes:**
- `GoogleChatBot/Commands/ICommand.cs` — `Task<ChatResponse> ExecuteAsync(ChatEvent)`
- `GoogleChatBot/Commands/HelpCommand.cs`
- `GoogleChatBot/Commands/StatusCommand.cs`
- `GoogleChatBot/Commands/CommandDispatcher.cs` — parses prefix, routes to ICommand
**Result:** Extensible command routing injected into ChatController.

---

### Stage 3 — Tool Architecture
**Goal:** Define tool abstractions in the `Tools` project.
**Classes:**
- `Tools/Abstractions/ITool.cs` — `string Name`, `string Description`, `Task<string> ExecuteAsync(string input)`
- `Tools/Abstractions/IMcpTool.cs` — extends ITool with JSON schema
- `Tools/HelloTool.cs` — returns greeting
- `Tools/TimeTool.cs` — returns current UTC time
- `Tools/ToolRegistry.cs` — `IReadOnlyList<ITool> GetAll()`
**Result:** Tools project has working, testable tools.

---

### Stage 4 — LLM Integration
**Goal:** Call OpenAI Chat Completions from Infrastructure.
**Packages:** `Azure.AI.OpenAI` or `OpenAI` NuGet (official SDK)
**Classes:**
- `Infrastructure/OpenAi/ILlmService.cs` — `Task<string> CompleteAsync(IList<ChatMessage> messages)`
- `Infrastructure/OpenAi/OpenAiService.cs` — implements ILlmService
- `Infrastructure/OpenAi/OpenAiOptions.cs` — ApiKey, Model, MaxTokens
**Result:** GoogleChatBot can call LLM and return AI-generated text.

---

### Stage 5 — LLM + Tools (Function Calling)
**Goal:** Wire tools into the OpenAI function-calling loop.
**Classes:**
- `Infrastructure/OpenAi/ToolFunctionMapper.cs` — converts IMcpTool → OpenAI function definition
- `Infrastructure/OpenAi/AgentLoop.cs` — iterates tool calls until final answer
**Result:** Bot can answer "What time is it?" by calling TimeTool via LLM function calling.

---

### Stage 6 — MCP Server
**Goal:** Expose tools via Model Context Protocol HTTP API.
**Endpoint contract:**
- `POST /mcp/tools/list` → `{ tools: [ { name, description, inputSchema } ] }`
- `POST /mcp/tools/call` → `{ toolName, input }` → `{ result }`
**Classes:**
- `McpServer/Controllers/McpController.cs`
- `McpServer/Models/ToolListResponse.cs`
- `McpServer/Models/ToolCallRequest.cs`
- `McpServer/Models/ToolCallResponse.cs`
**Result:** McpServer is a standalone HTTP MCP endpoint.

---

### Stage 7 — Workflow + State Machine
**Goal:** ErrorTickets progress through defined states with transitions.
**Classes:**
- `Domain/Workflow/IWorkflowTransition.cs`
- `Domain/Workflow/TicketWorkflow.cs` — defines allowed transitions (e.g. New→Analyzing)
- Storage: `ITicketRepository` interface in Domain, in-memory impl in Infrastructure
**Result:** Tickets can be created, transitioned, and queried by state.

---

### Stage 8 — Google Chat Cards (Buttons)
**Goal:** Return rich cards with action buttons (Analyze, Fix, Close).
**Classes:**
- `GoogleChatBot/Cards/CardBuilder.cs` — fluent builder
- `GoogleChatBot/Cards/ButtonAction.cs`
- `GoogleChatBot/Models/Outgoing/CardResponse.cs`
**Result:** Bot sends a card with ticket details + Analyze/Fix buttons.

---

### Stage 9 — Worker (Polling)
**Goal:** Every 5 minutes, scan for new errors and create ErrorTickets.
**Classes:**
- `Worker/ErrorPollingWorker.cs` — replaces default Worker.cs
- `Worker/IErrorSource.cs` — `Task<IList<ErrorTicket>> FetchNewErrorsAsync()`
- `Worker/StubErrorSource.cs` — returns fake errors for testing
**Result:** Worker creates ErrorTickets on schedule.

---

### Stage 10 — Google Drive / Sheets Integration
**Goal:** Read actual error data from Google Sheets.
**Packages:** `Google.Apis.Sheets.v4`, `Google.Apis.Drive.v3`
**Classes:**
- `Infrastructure/Google/GoogleSheetsService.cs`
- `Infrastructure/Google/GoogleDriveService.cs`
- `Infrastructure/Google/GoogleCredentialOptions.cs` — ServiceAccountJson, SpreadsheetId
**Result:** Worker uses real Google Sheets as error source.

---

### Stage 11 — Code Access (Repo Tools)
**Goal:** Read files from a GitHub repository for LLM analysis.
**Packages:** `Octokit` NuGet
**Classes:**
- `Tools/Git/GitHubRepoTool.cs` — reads file content by path
- `Tools/Git/GitHubSearchTool.cs` — searches code
- `Infrastructure/GitHub/GitHubClientFactory.cs`
- `Infrastructure/GitHub/GitHubOptions.cs` — Owner, Repo, Token
**Result:** LLM can read source code during analysis.

---

### Stage 12 — Fix Pipeline (Branch / Commit / PR)
**Goal:** Create branch, apply fix, open Pull Request.
**Classes:**
- `Tools/Git/CreateBranchTool.cs`
- `Tools/Git/CommitFileTool.cs`
- `Tools/Git/CreatePullRequestTool.cs`
- `Infrastructure/GitHub/GitHubService.cs` — wraps Octokit
**Result:** Full automated fix pipeline triggered by "Fix" button click.

---

### Stage 13 — Human-in-the-Loop
**Goal:** Pause pipeline at critical steps, wait for human approval via Google Chat buttons.
**Classes:**
- `Domain/Approvals/ApprovalRequest.cs`
- `Domain/Approvals/ApprovalState.cs` (enum: Pending, Approved, Rejected)
- `GoogleChatBot/Controllers/ActionController.cs` — handles button callbacks
- `Infrastructure/Approvals/InMemoryApprovalStore.cs`
**Result:** Bot asks "Should I open this PR?" and only proceeds on Approve click.

---

## Dependency Graph

```
Domain  (no deps)
  ↑
Tools → Domain
  ↑
Infrastructure → Domain, Tools
  ↑
GoogleChatBot → Domain, Infrastructure, Tools
McpServer     → Tools
Worker        → Domain, Infrastructure
```

---

## Immediate Implementation Steps

1. **Create `IMPLEMENTATION_PLAN.md`** at repo root (full stage reference)
2. **Delete `Domain/Class1.cs`**
3. **Create `Domain/Tickets/TicketState.cs`**
4. **Create `Domain/Tickets/ErrorTicket.cs`**
5. **Update `GoogleChatBot/Program.cs`** (add controllers)
6. **Create `GoogleChatBot/Models/Incoming/ChatEvent.cs`**
7. **Create `GoogleChatBot/Models/Incoming/ChatMessage.cs`**
8. **Create `GoogleChatBot/Models/Incoming/ChatSender.cs`**
9. **Create `GoogleChatBot/Models/Incoming/ChatSpace.cs`**
10. **Create `GoogleChatBot/Models/Outgoing/ChatResponse.cs`**
11. **Create `GoogleChatBot/Controllers/ChatController.cs`**
