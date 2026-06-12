# AI Chat Bot — Architecture

> For the stage-by-stage build history see [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md).

## Purpose

An automated bug-report triage system that bridges Google Drive, Google Chat, OpenAI, and GitHub.
A file dropped in a monitored Drive folder triggers a complete pipeline:
**notification → AI analysis → code fix committed to a branch** — all visible inside a single Google Chat thread.

---

## High-Level Flow

```
 Google Drive
  (New/ folder)
       │ new file detected
       ▼
 ┌──────────┐  LLM: "one-sentence description"   ┌──────────────┐
 │  Worker  │ ──────────────────────────────────▶│   OpenAI     │
 │(poller)  │                                    └──────────────┘
 │          │
 │          │  POST notification card ──────────────────────────▶  Chat thread
 │          │  POST file-content card + [Analyze] button ────────▶  (same thread)
 │          │  MOVE file → InProcess/
 └──────────┘

 ─────────────────────── User clicks [Analyze] ──────────────────────────────

 CARD_CLICKED webhook
       │
       ▼
 ┌─────────────────┐  UPDATE_MESSAGE (card → "Analyzing") ──────▶  Chat
 │ GoogleChatBot   │
 │ ActionController│──── background Task ────────────────────────────────────
 └─────────────────┘                                                         │
                                                                             ▼
                                                                  ┌──────────────────┐
                                                                  │ AnalysisService  │
                                                                  │  (LLM loop)      │
                                                                  │  github_search   │──▶ GitHub API
                                                                  │  github_read     │
                                                                  └────────┬─────────┘
                                                                           │ ticket → Analyzed
                                                                           │ POST analysis text ──▶ Chat
                                                                           │ POST status card  ──▶ Chat

 ─────────────────────── User clicks [Fix] ──────────────────────────────────

 CARD_CLICKED webhook
       │
       ▼
 ┌─────────────────┐  UPDATE_MESSAGE (card → "Fixing") ─────────▶  Chat
 │ ActionController│──── background Task ────────────────────────────────────
 └─────────────────┘                                                         │
                                                                             ▼
                                                                  ┌──────────────────┐
                                                                  │   FixService     │
                                                                  │  CreateBranch    │──▶ GitHub API
                                                                  │  (LLM loop)      │
                                                                  │  github_read     │
                                                                  │  github_commit   │──▶ GitHub API
                                                                  └────────┬─────────┘
                                                                           │ ticket → Fixed
                                                                           │ POST "branch fix/…" ──▶ Chat
                                                                           │ POST status card   ──▶ Chat

 ─────────────────────── User clicks [Close] ────────────────────────────────
 ticket → Closed
```

---

## Solution Layout

```
AiChatBotSolution.slnx                (.NET 10.0)
│
├── Domain/                           pure domain model — zero external deps
│   ├── Repositories/                 ITicketRepository
│   ├── Tickets/                      ErrorTicket, TicketState
│   └── Workflow/                     TicketWorkflow, WorkflowException
│
├── Tools/                            tool interface + registry (no built-in tools)
│   ├── Abstractions/ITool.cs
│   └── ToolRegistry.cs
│
├── GitHubTools/                      GitHub LLM tools (depends on Infrastructure)
│   ├── GitHubRepoTool.cs             ITool github_read_file
│   ├── GitHubSearchTool.cs           ITool github_search_code
│   └── CommitFileTool.cs             ITool github_commit_file
│
├── Infrastructure/                   all external-API clients
│   ├── Analysis/                     IAnalysisService, AnalysisService
│   ├── Fix/                          IFixService, FixService
│   ├── GitHub/                       IGitHubService, GitHubService, GitHubOptions
│   ├── Google/                       Drive + Chat API clients, DriveFileReader
│   ├── OpenAi/                       AgentService, OpenAiService, OpenAiOptions
│   ├── Persistence/                  EF Core / SQLite — AppDbContext, SqliteTicketRepository
│   └── Repositories/                 InMemoryTicketRepository (dev fallback)
│
├── GoogleChatBot/                    ASP.NET Core Web — webhook receiver
│   ├── Cards/                        CardBuilder, TicketCardBuilder, ButtonAction
│   ├── Commands/                     slash-command system (ICommand, CommandDispatcher)
│   ├── Controllers/                  ChatController, ActionController (service)
│   ├── Handlers/                     IChatEventHandler, ChatEventHandler
│   └── Models/                       Incoming DTOs, BotResponse, CardResponse
│
├── McpServer/                        ASP.NET Core Web — MCP HTTP endpoint
│   └── Controllers/McpController.cs  GET /mcp/tools · POST /mcp/tools/call
│
└── Worker/                           .NET Worker Service — background Drive poller
    └── GoogleDriveWatcherWorker.cs
```

---

## Project Dependency Graph

```
Domain          ← no project or NuGet dependencies
   ↑
Tools           ← no project dependencies
   ↑
Infrastructure  ← Domain, Tools
                  NuGet: OpenAI, Octokit, Google.Apis.Drive.v3,
                         Microsoft.EntityFrameworkCore.Sqlite
   ↑
GitHubTools     ← Infrastructure, Tools
                  (ITool implementations that call IGitHubService)
   ↑
GoogleChatBot   ← Domain, Infrastructure, Tools, GitHubTools
McpServer       ← Tools, Infrastructure, GitHubTools
Worker          ← Domain, Infrastructure
```

> **Rule:** Domain and Tools are dependency-free by convention.
> GitHub tools live in `GitHubTools/` (not `Tools/`) to respect this rule —
> adding them to `Tools` would create a `Tools → Infrastructure` cycle.
> `AnalysisService` and `FixService` receive the tools via
> `[FromKeyedServices]` constructor attributes, keeping `Infrastructure`
> free of any `GitHubTools` project reference.

---

## Projects in Detail

### Domain

| Type | Role |
|---|---|
| `ErrorTicket` | Aggregate root — see field table below |
| `TicketState` | Enum: `New` → `Analyzing` → `Analyzed` → `Fixing` → `Fixed` → `Closed` / `Failed` |
| `ITicketRepository` | `AddAsync` / `GetByIdAsync` / `GetAllAsync` / `UpdateAsync` |
| `TicketWorkflow` | Static transition table; `Transition`, `CanTransition`, `GetAvailableTransitions` |
| `WorkflowException` | Thrown on disallowed transition |

**`ErrorTicket` fields**

| Property | Type | Purpose |
|---|---|---|
| `Id` | `Guid` | Primary key |
| `Title` | `string` | Short human-readable title |
| `Description` | `string` | Full error text / stack trace / extracted file content |
| `Source` | `string` | `"GoogleDrive"` \| `"Manual"` |
| `SourceFileId` | `string?` | Drive file ID |
| `SpaceName` | `string?` | Google Chat space (`spaces/xxx`) |
| `MessageName` | `string?` | Notification card message resource name |
| `ThreadName` | `string?` | Thread resource name — all replies go here |
| `State` | `TicketState` | Current workflow state |
| `AnalysisResult` | `string?` | LLM-generated analysis (set after Analyzing) |
| `BranchName` | `string?` | Git branch created by FixService (set after Fixing) |

---

### Tools

| File | Tool name | Behaviour |
|---|---|---|
| `ToolRegistry.cs` | — | `GetAll()` / `Find(name)` (case-insensitive) |

`ITool` contract: `Name`, `Description`, `InputSchema` (raw JSON Schema string),
`ExecuteAsync(string input) → Task<string>`.

---

### Infrastructure

#### `OpenAi/`

| Type | Role |
|---|---|
| `OpenAiOptions` | `ApiKey`, `Model`, `MaxTokens`, `SystemPrompt` |
| `OpenAiService` | Single-turn completion (fallback; not active in production) |
| `AgentService` | Multi-turn tool-calling loop (registered as `ILlmService`) |

`AgentService` loop (max 5 iterations):
`[System, User]` → `CompleteChatAsync` → on `ToolCalls`: execute each tool → append `ToolChatMessage` → repeat → on `Stop`: return text.

#### `GitHub/`

| Type | Role |
|---|---|
| `GitHubOptions` | `Owner`, `Repo`, `Token`, `DefaultBranch` |
| `IGitHubService` / `GitHubService` | Octokit wrapper: `ReadFileAsync`, `SearchCodeAsync`, `CreateBranchAsync`, `CommitFileAsync` |

#### `GitHubTools/` (separate project — depends on Infrastructure)

| Type | Role |
|---|---|
| `GitHubRepoTool` | `ITool` `github_read_file` — reads a file by path (+ optional branch) |
| `GitHubSearchTool` | `ITool` `github_search_code` — returns up to 10 matching file paths |
| `CommitFileTool` | `ITool` `github_commit_file` — creates / updates a file on a branch |

#### `Google/`

| Type | Role |
|---|---|
| `GoogleCredentialOptions` | `ServiceAccountJson`, Drive folder IDs, `ChatSpaceName`, `PollingIntervalSeconds` |
| `IGoogleDriveService` / `GoogleDriveService` | List files in a folder, move files between folders |
| `IDriveFileReader` / `DriveFileReader` | Extracts text: `text/*` → direct download; `application/vnd.google-apps.document` → export; `image/*` → Vision OCR via OpenAI |
| `IGoogleChatApiService` / `GoogleChatApiService` | `PostNotificationCardAsync`, `PostFileContentCardAsync`, `PostThreadReplyAsync`, `PostThreadMessageAsync` — Bearer token from service account |

#### `Analysis/`

`AnalysisService` — LLM agent loop (max 8 iterations) with `GitHubRepoTool` + `GitHubSearchTool`.
Input: `ErrorTicket`. Output: analysis report string.
Called by `ActionController` as a fire-and-forget background task.

#### `Fix/`

`FixService` — LLM agent loop (max 10 iterations) with `GitHubRepoTool` + `GitHubSearchTool` + `CommitFileTool`.
Pre-creates branch `fix/{8-char-uuid}-{slug}` via `IGitHubService` before starting the loop.
Returns the branch name. Called by `ActionController` as a fire-and-forget background task.

#### `Persistence/`

| Type | Role |
|---|---|
| `AppDbContext` | EF Core DbContext with `DbSet<ErrorTicket>`; SQLite WAL mode enabled |
| `SqliteTicketRepository` | `ITicketRepository` backed by `IDbContextFactory<AppDbContext>` |
| `PersistenceExtensions` | `AddSqliteTickets(connStr)` + `EnsureDatabase()` extension methods |

Both `Worker` and `GoogleChatBot` open the **same SQLite file**; WAL mode allows concurrent reads and writes without blocking.

---

### GoogleChatBot

ASP.NET Core Web application. All Google Chat events arrive at a single `POST /chat` endpoint.

#### Event routing

| `event.type` | Handler |
|---|---|
| `MESSAGE` | `ChatEventHandler` → `CommandDispatcher` → `ILlmService` fallback |
| `CARD_CLICKED` | `ChatController` → `ActionController.HandleAsync` |
| `ADDED_TO_SPACE` | Greeting text |
| `REMOVED_FROM_SPACE` | `200 OK {}` |

#### Card system

```
CardBuilder (fluent)
  └─▶ CardResponse  (cardsV2 JSON model with [JsonPropertyName] on all fields)
        └─▶ TicketCardBuilder.Build(ticket, workflow)
               header   : Title · State · Source · timestamp
               paragraph: Description
               paragraph: AnalysisResult  (if set)
               buttons  : one per GetAvailableTransitions(state)
```

`BotResponse` discriminated union:
- `TextOnly(string)` → `{ "text": "…" }`
- `Card(CardResponse)` → `{ "cardsV2": […] }` with `UPDATE_MESSAGE` action for `CARD_CLICKED` responses

#### ActionController

1. Loads ticket, calls `TicketWorkflow.Transition`, persists
2. Posts instant status text to thread (non-fatal on failure)
3. For `analyze` / `fix` only: fires background `Task` (fire-and-forget)
4. Returns `UPDATE_MESSAGE` card synchronously (< 1 ms webhook response)

Background completion:
- Saves `AnalysisResult` / `BranchName` on ticket
- Transitions ticket to `Analyzed` / `Fixed`
- Posts result text + new status card to thread
- On any exception: transitions to `Failed`, posts error text

---

### McpServer

Standalone MCP (Model Context Protocol) HTTP endpoint. Exposes the three GitHub tools.

```
GET  /mcp/tools        → { tools: [{ name, description, inputSchema }] }
POST /mcp/tools/call   → { toolName, input } → { result }
```

Swagger UI served at `/` (root). Default port: `5295`.
Tools exposed: `github_read_file`, `github_search_code`, `github_commit_file`.

---

### Worker

`GoogleDriveWatcherWorker` polling loop (configurable interval, default 30 s in dev / 300 s in prod):

1. `IGoogleDriveService.ListFilesAsync("New/")` — list new files
2. `IDriveFileReader.ReadAsync(fileId, mimeType, fileName)` — extract text
3. `ILlmService.CompleteAsync(text)` — generate one-sentence description
4. `ITicketRepository.AddAsync(ticket)` — persist with `State = New`
5. `IGoogleChatApiService.PostNotificationCardAsync(...)` — header card in space
6. `IGoogleChatApiService.PostFileContentCardAsync(...)` — thread card with file content + **[Analyze]** button
7. Save `SpaceName`, `MessageName`, `ThreadName` on ticket; `UpdateAsync`
8. `IGoogleDriveService.MoveFileAsync(fileId, "InProcess/")` — move file

---

## Workflow State Machine

```
         [Analyze clicked]       [Fix clicked]
New ──▶ Analyzing ──▶ Analyzed ──▶ Fixing ──▶ Fixed ──▶ Closed
           │              ↑           │           ↑
           ▼              │           ▼           │
         Failed ──────────┘         Failed ───────┘
           │        [retry]
           └──▶ New
```

| Reachable state | Button label | `actionMethodName` | LLM work? |
|---|---|---|---|
| `Analyzing` | Analyze | `analyze` | Yes — AnalysisService |
| `Analyzed` | Mark Analyzed | `mark_analyzed` | No |
| `Fixing` | Start Fix | `fix` | Yes — FixService |
| `Fixed` | Mark Fixed | `mark_fixed` | No |
| `Closed` | Close | `close` | No |
| `New` | Retry | `retry` | No |

`mark_analyzed` and `mark_fixed` are manual overrides — they advance the ticket without running LLM work, useful for tickets that have no associated Chat thread.

---

## External Integrations

| Service | SDK / Transport | Used by | Authentication |
|---|---|---|---|
| OpenAI Chat Completions | `OpenAI` NuGet v2.2.0 | Worker, AnalysisService, FixService | API key (`OpenAi:ApiKey`) |
| OpenAI Vision (image OCR) | Same SDK — `CreateImagePart` | `DriveFileReader` | Same API key |
| Google Drive API v3 | `Google.Apis.Drive.v3` NuGet | Worker (`GoogleDriveService`, `DriveFileReader`) | Service account JSON |
| Google Chat REST API | `HttpClient` + Bearer token | Worker, GoogleChatBot | Same service account |
| GitHub REST API | `Octokit` NuGet v14.0.0 | AnalysisService, FixService | Personal Access Token (`GitHub:Token`) |

---

## Configuration Reference

Both `GoogleChatBot` and `Worker` share the same config schema (except `Worker` does not use `GitHub`).
Sensitive values should be stored in `dotnet user-secrets` or environment variables, never committed.

```json
{
  "ConnectionStrings": {
    "Tickets": "Data Source=/absolute/path/to/tickets.db"
  },
  "GitHub": {
    "Owner":         "your-org-or-user",
    "Repo":          "your-repository",
    "Token":         "ghp_…",
    "DefaultBranch": "main"
  },
  "Google": {
    "ServiceAccountJson":     "path/to/service-account.json",
    "NewFolderId":            "<Drive folder ID>",
    "InProcessFolderId":      "<Drive folder ID>",
    "DoneFolderId":           "<Drive folder ID>",
    "ChatSpaceName":          "spaces/XXXXXXXXX",
    "PollingIntervalSeconds":  30
  },
  "OpenAi": {
    "ApiKey":       "sk-proj-…",
    "Model":        "gpt-4o-mini",
    "MaxTokens":    1024,
    "SystemPrompt": "…"
  }
}
```

**Both processes must point `ConnectionStrings:Tickets` to the same absolute SQLite path.**

---

## Key Design Decisions

| Decision | Rationale |
|---|---|
| Single `POST /chat` webhook for all event types | Google Chat routes all events to one URL; routing by `event.type` happens inside the app |
| `ActionController` is a service, not an `[ApiController]` | No dedicated route needed; injected into `ChatController` which owns the sole webhook endpoint |
| Fire-and-forget background tasks for `analyze` / `fix` | Webhook must respond in < 30 s; LLM + GitHub calls can take minutes |
| GitHub tools in `GitHubTools/` not `Tools/` | `Tools` has no project deps by convention; moving them there would create `Tools → Infrastructure` (circular) |
| `AnalysisService` / `FixService` use `[FromKeyedServices]` | `Infrastructure` never references `GitHubTools`; tools are injected by key at runtime from the host DI container |
| GitHub tools registered as **keyed** `ITool` in GoogleChatBot | Keeps `ToolRegistry` empty so `AgentService` (general chat) never calls GitHub APIs unexpectedly |
| GitHub tools registered as **non-keyed** `ITool` in McpServer | `ToolRegistry` discovers them via `IEnumerable<ITool>` and exposes them to external MCP clients |
| Branch pre-created by `FixService` before LLM loop | Branch name is deterministic (`fix/{8-char-id}-{slug}`), known before LLM starts; avoids exposing a `CreateBranch` tool to the model |
| No PR creation | Merge decision stays with the developer; branch name in the thread is sufficient signal |
| `CardResponse` uses `[JsonPropertyName]` on every property | Serialises correctly when passed as `object` to `PostThreadMessageAsync` without any `Infrastructure → GoogleChatBot` dependency |
| Shared SQLite in WAL mode | Two OS processes (`Worker` + `GoogleChatBot`) must share ticket state without a separate database server |
| `TruncateForChat` (3 500 chars) in `GoogleChatApiService` | Google Chat card widgets have a character limit; truncation centralised in the service |
