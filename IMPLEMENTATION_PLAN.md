# AI Chat Bot — Implementation Plan

> **Architecture overview:** see [ARCHITECTURE.md](ARCHITECTURE.md) for the full system design,
> dependency graph, data-flow diagrams, and configuration reference.

## Architecture Overview

**Solution:** `AiChatBotSolution.slnx` | **Target:** net10.0

### Projects

| Project | SDK | Role |
|---|---|---|
| `Domain` | `Microsoft.NET.Sdk` | Models, enums — zero dependencies |
| `Tools` | `Microsoft.NET.Sdk` | Tool interfaces and implementations |
| `Infrastructure` | `Microsoft.NET.Sdk` | OpenAI, GitHub, Google API clients |
| `GoogleChatBot` | `Microsoft.NET.Sdk.Web` | Webhook receiver, response sender |
| `McpServer` | `Microsoft.NET.Sdk.Web` | MCP protocol HTTP endpoint |
| `Worker` | `Microsoft.NET.Sdk.Worker` | Periodic background polling |

### Project Dependency Graph

```
Domain          (no external deps)
   ↑
Tools           (no project deps)
   ↑
Infrastructure  → Domain, Tools
   ↑
GoogleChatBot   → Domain, Infrastructure, Tools
McpServer       → Tools
Worker          → Domain, Infrastructure  (planned Stage 9)
```

---

## Stage 1 — Base Bot (Google Chat Webhook) ✅ DONE

**Goal:** Receive Google Chat webhook, parse the message, return plain text.

### Files
| File | Purpose |
|---|---|
| `GoogleChatBot/Controllers/ChatController.cs` | `POST /chat` — dispatches by `event.type` |
| `GoogleChatBot/Models/Incoming/ChatEvent.cs` | Root envelope: `type`, `message`, `space`, `action` |
| `GoogleChatBot/Models/Incoming/ChatMessage.cs` | `name`, `text`, `sender` |
| `GoogleChatBot/Models/Incoming/ChatSender.cs` | `name`, `displayName` |
| `GoogleChatBot/Models/Incoming/ChatSpace.cs` | `name`, `type` (`"DM"` \| `"ROOM"`) |
| `GoogleChatBot/Models/Incoming/ChatAction.cs` | `actionMethodName`, `parameters` |
| `GoogleChatBot/Models/Incoming/ChatActionParameter.cs` | `key`, `value` |
| `GoogleChatBot/Models/Outgoing/ChatResponse.cs` | `{ "text": "..." }` + `static From(string)` |

All DTOs use `[JsonPropertyName]` for snake_case ↔ PascalCase mapping.

| Event type | Behaviour |
|---|---|
| `ADDED_TO_SPACE` | Greeting message |
| `MESSAGE` | CommandDispatcher → LLM fallback |
| `CARD_CLICKED` | Placeholder (Stage 8) |
| `REMOVED_FROM_SPACE` | `Ok({})` |

### Result
Working `POST /chat` webhook, verifiable via curl or ngrok.

---

## Stage 2 — Command System ✅ DONE

**Goal:** Parse and dispatch slash commands.

### Files
| File | Purpose |
|---|---|
| `GoogleChatBot/Commands/ICommand.cs` | `Name`, `Description`, `CanHandle(string)`, `Task<string> ExecuteAsync(string)` |
| `GoogleChatBot/Commands/HelloCommand.cs` | `/hello [name]` — delegates to `HelloTool` |
| `GoogleChatBot/Commands/TimeCommand.cs` | `/time` — delegates to `TimeTool` |
| `GoogleChatBot/Commands/HelpCommand.cs` | `/help` — lists all commands (prepends itself at render time) |
| `GoogleChatBot/Commands/CommandDispatcher.cs` | Returns `null` if no `/` prefix; "Unknown command" if no match |

**Note:** `ExecuteAsync` accepts `string` (not `ChatEvent`) and returns `string` (not `ChatResponse`) — simplified interface.

### Result
Extensible, DI-registered command routing injected into `ChatController`.

---

## Stage 3 — Tool Architecture ✅ DONE

**Goal:** Define tool abstractions in `Tools`; implement `HelloTool` and `TimeTool`.

### Files
| File | Purpose |
|---|---|
| `Tools/Abstractions/ITool.cs` | `Name`, `Description`, `InputSchema` (raw JSON Schema string), `Task<string> ExecuteAsync(string)` |
| `Tools/HelloTool.cs` | `name="hello"` — returns `"Hello, *{name}*!"` |
| `Tools/TimeTool.cs` | `name="time"` — returns `DateTimeOffset.UtcNow` as `yyyy-MM-dd HH:mm:ss UTC` |
| `Tools/ToolRegistry.cs` | `GetAll()`, `Find(name)` (case-insensitive) |

**Note:** `IMcpTool` was not created — `InputSchema` is embedded directly in `ITool`. One interface serves all consumers (commands, LLM, MCP).

### Result
Tools project has working, testable tools with JSON Schema support.

---

## Stage 4 — LLM Integration ✅ DONE

**Goal:** Call OpenAI Chat Completions API from `Infrastructure`.

**NuGet:** `OpenAI 2.2.0` (official .NET SDK)

### Files
| File | Purpose |
|---|---|
| `Infrastructure/OpenAi/ILlmService.cs` | `Task<string> CompleteAsync(string userMessage)` |
| `Infrastructure/OpenAi/OpenAiService.cs` | Simple single-turn completion — fallback, not active |
| `Infrastructure/OpenAi/OpenAiOptions.cs` | Config section `"OpenAi"`: `ApiKey`, `Model`, `MaxTokens`, `SystemPrompt` |

API key stored via `dotnet user-secrets set "OpenAi:ApiKey" "sk-..."`.

**Note:** `CompleteAsync` accepts `string` (system prompt lives in `OpenAiOptions`, not call site). `LlmMessage.cs` was not created.

### Result
Infrastructure can call OpenAI; active service is `AgentService` (Stage 5).

---

## Stage 5 — LLM + Tools (Function Calling) ✅ DONE

**Goal:** Wire tools into the OpenAI function-calling loop.

### Files
| File | Purpose |
|---|---|
| `Infrastructure/OpenAi/AgentService.cs` | Implements `ILlmService`; active service registered in `GoogleChatBot` |

**`AgentService.CompleteAsync` algorithm:**
1. Builds `[SystemChatMessage, UserChatMessage]`
2. Reads all tools from `ToolRegistry`, creates `ChatTool.CreateFunctionTool(name, description, BinaryData(inputSchema))` per tool
3. Runs loop (max `MaxIterations = 5`):
   - `Stop` → return text
   - `ToolCalls` → `Find(name)` → `ExtractFirstStringArg(json)` → `ExecuteAsync` → append `ToolChatMessage` → next iteration
4. `ExtractFirstStringArg` — returns value of first string property in the JSON object (or `""` for empty)

**Note:** `ToolFunctionMapper.cs` and `AgentLoop.cs` as separate files were not created — entire loop is in `AgentService.cs`.

### Result
Bot answers "What time is it?" by calling `TimeTool` via LLM function calling.

---

## Stage 6 — MCP Server ✅ DONE

**Goal:** Expose tools via Model Context Protocol over HTTP.

**NuGet:** `Swashbuckle.AspNetCore 10.2.1`

### Endpoint Contract
```
GET  /mcp/tools        → { tools: [{ name, description, inputSchema }] }
POST /mcp/tools/call   → { toolName, input } → { result } / 404 { error }
```

Swagger UI served at `/` (root) → `http://localhost:5295/`

### Files
| File | Purpose |
|---|---|
| `McpServer/Controllers/McpController.cs` | `ListTools` + `CallTool` actions |
| `McpServer/Models/McpDtos.cs` | `ToolInfo`, `ToolListResponse`, `ToolCallRequest`, `ToolCallResponse` |
| `McpServer/Program.cs` | Controllers + tool DI + Swagger UI at root |

### Result
`McpServer` is a standalone HTTP MCP endpoint with interactive Swagger UI.

---

## Stage 7 — Workflow + State Machine ✅ DONE

**Goal:** `ErrorTickets` progress through defined states with guarded transitions.

### Allowed Transitions
```
New → Analyzing → Analyzed → Fixing → Fixed → Closed
          ↘ Failed                 ↘ Failed
Failed → New  (retry)
```

### Files
| File | Purpose |
|---|---|
| `Domain/Tickets/TicketState.cs` | Enum: `New=0 … Closed=6` |
| `Domain/Tickets/ErrorTicket.cs` | Aggregate: `Id`, `Title`, `Description`, `Source`, `State`, `CreatedAt`, `UpdatedAt`, … |
| `Domain/Repositories/ITicketRepository.cs` | `AddAsync`, `GetByIdAsync`, `GetAllAsync`, `UpdateAsync` |
| `Domain/Workflow/IWorkflowTransition.cs` | Interface: `From` / `To` `TicketState` |
| `Domain/Workflow/WorkflowException.cs` | Thrown when transition is not permitted |
| `Domain/Workflow/TicketWorkflow.cs` | Static transition table; `CanTransition`, `GetAvailableTransitions`, `Transition` |
| `Infrastructure/Repositories/InMemoryTicketRepository.cs` | `ConcurrentDictionary`-backed, thread-safe |
| `GoogleChatBot/Commands/TicketCommand.cs` | `/ticket <description>` — creates ticket with state `New` |
| `GoogleChatBot/Commands/StatusCommand.cs` | `/status` — lists all tickets with states |

`GetAvailableTransitions(state)` returns the set of reachable states — used by Stage 8 to decide which action buttons to render.

### Result
Tickets can be created, safely transitioned, and queried by state.

---

## Stage 6b — Swagger for GoogleChatBot ✅ DONE

**Goal:** Add interactive Swagger UI to `GoogleChatBot` for local development.

**NuGet:** `Swashbuckle.AspNetCore 10.2.1`

Swagger UI available at `http://localhost:<port>/swagger`

### Changes
| File | Change |
|---|---|
| `GoogleChatBot/GoogleChatBot.csproj` | Added `Swashbuckle.AspNetCore`, `GenerateDocumentationFile`, `NoWarn 1591` |
| `GoogleChatBot/Program.cs` | `AddEndpointsApiExplorer()` + `AddSwaggerGen(...)` + `UseSwagger()` + `UseSwaggerUI(...)` |
| `GoogleChatBot/Controllers/ChatController.cs` | XML `<summary>` + `<remarks>` on controller class and action |

---

## Stage 8 — Google Chat Cards (Buttons) ✅ COMPLETED

**Goal:** Return rich interactive cards with action buttons (Analyze, Fix, Close).

---

Stage 8 — COMPLETED

What was implemented:
- `BotResponse` discriminated union (`TextOnly` | `Card`) — commands can now return either plain text or a rich card
- `CardResponse` + full cardsV2 JSON model (`CardV2Wrapper`, `CardV2`, `CardHeader`, `CardSection`, `CardWidget`, `TextParagraphWidget`, `ButtonListWidget`, `CardButtonWidget`, `CardButtonAction`, `CardActionParameter`)
- `ButtonAction` record — label + function name + parameters
- `CardBuilder` fluent builder — builds a `CardResponse` from title, paragraphs, and buttons
- `TicketCardBuilder` static helper — builds a ticket card with action buttons derived from `TicketWorkflow.GetAvailableTransitions`
- `ActionController` service — handles `CARD_CLICKED` events: loads ticket, applies workflow transition, returns refreshed card
- `ICommand.ExecuteAsync` return type changed from `Task<string>` to `Task<BotResponse>`
- `CommandDispatcher.DispatchAsync` return type changed from `Task<string?>` to `Task<BotResponse?>`
- `ChatController` updated — new `OnCardClickedAsync` delegates to `ActionController`; `ToApiResponse` converts `BotResponse` to the correct wire format

Files changed:
- `GoogleChatBot/Models/Outgoing/BotResponse.cs` *(new)*
- `GoogleChatBot/Models/Outgoing/CardResponse.cs` *(new)*
- `GoogleChatBot/Cards/ButtonAction.cs` *(new)*
- `GoogleChatBot/Cards/CardBuilder.cs` *(new)*
- `GoogleChatBot/Cards/TicketCardBuilder.cs` *(new)*
- `GoogleChatBot/Controllers/ActionController.cs` *(new)*
- `GoogleChatBot/Commands/ICommand.cs` *(updated — ExecuteAsync returns BotResponse)*
- `GoogleChatBot/Commands/HelloCommand.cs` *(updated)*
- `GoogleChatBot/Commands/TimeCommand.cs` *(updated)*
- `GoogleChatBot/Commands/HelpCommand.cs` *(updated)*
- `GoogleChatBot/Commands/TicketCommand.cs` *(updated — now returns card; requires TicketWorkflow)*
- `GoogleChatBot/Commands/StatusCommand.cs` *(updated)*
- `GoogleChatBot/Commands/CommandDispatcher.cs` *(updated)*
- `GoogleChatBot/Controllers/ChatController.cs` *(updated — CARD_CLICKED wired, BotResponse handling)*
- `GoogleChatBot/Program.cs` *(updated — ActionController registered, TicketCommand gets TicketWorkflow)*

Notes:
- `ActionController` is a service class (not an `[ApiController]`); all Google Chat events arrive at a single `POST /chat` webhook, so it is injected into `ChatController` rather than mapped as a separate route
- Button labels are derived at runtime from `TicketWorkflow.GetAvailableTransitions` — the card always reflects the actual workflow state
- Action function names (`analyze`, `mark_analyzed`, `fix`, `mark_fixed`, `close`, `retry`) are the contract between `TicketCardBuilder` and `ActionController`

How to test:
```
# Create a ticket — response should be a cardsV2 JSON
POST /chat
{ "type": "MESSAGE", "message": { "text": "/ticket NullReferenceException in PaymentService" } }

# Click "Analyze" button — response should be updated card with state=Analyzing
POST /chat
{ "type": "CARD_CLICKED", "action": { "actionMethodName": "analyze", "parameters": [{ "key": "ticketId", "value": "<id>" }] } }
```

### Files
| File | Purpose |
|---|---|
| `GoogleChatBot/Models/Outgoing/BotResponse.cs` | Discriminated union: `TextOnly` \| `Card` |
| `GoogleChatBot/Models/Outgoing/CardResponse.cs` | Full cardsV2 JSON model |
| `GoogleChatBot/Cards/ButtonAction.cs` | Record: label + function name + parameters |
| `GoogleChatBot/Cards/CardBuilder.cs` | Fluent builder → `CardResponse` |
| `GoogleChatBot/Cards/TicketCardBuilder.cs` | Builds ticket card with workflow buttons |
| `GoogleChatBot/Controllers/ActionController.cs` | Handles `CARD_CLICKED`: applies transition, returns refreshed card |

### Result
Bot sends cards with ticket info + Analyze / Fix / Close buttons derived from `TicketWorkflow.GetAvailableTransitions`.

---

## Stage 9b — Persistent Ticket Repository (SQLite)

**Goal:** Replace the per-process `InMemoryTicketRepository` with a shared SQLite database so that
tickets created by the Worker are visible to the GoogleChatBot when processing button clicks.

### Problem
`Worker` and `GoogleChatBot` are separate OS processes. Each had its own
`InMemoryTicketRepository` instance, so a ticket created by the Worker was invisible to the
GoogleChatBot when the user clicked the **Analyze** button.

### Solution
Both processes connect to the same SQLite file via EF Core. SQLite WAL mode is enabled so
concurrent reads (GoogleChatBot) and writes (Worker) don't block each other.

### Files
| File | Purpose |
|---|---|
| `Infrastructure/Persistence/AppDbContext.cs` | EF Core `DbContext` with `DbSet<ErrorTicket>` |
| `Infrastructure/Persistence/SqliteTicketRepository.cs` | `ITicketRepository` backed by `IDbContextFactory<AppDbContext>` |
| `Infrastructure/Persistence/PersistenceExtensions.cs` | `AddSqliteTickets(connectionString)` + `EnsureDatabase()` helpers |

**NuGet:** `Microsoft.EntityFrameworkCore.Sqlite` (added to `Infrastructure`)

### Configuration (both processes)
```json
"ConnectionStrings": {
  "Tickets": "Data Source=C:\\path\\to\\shared\\tickets.db"
}
```
Set the same absolute path in `Worker/appsettings.json` and `GoogleChatBot/appsettings.json`.
In development, use user-secrets or `appsettings.Development.json` for the real path.

### Result
Tickets survive across process boundaries — clicking **Analyze** in Chat correctly finds the
ticket created by the Drive Watcher.

---

## Stage 9 — Google Drive Watcher + Chat Notification

**Goal:** Watch a Google Drive folder's `New/` subfolder; on new file — create a ticket, notify Chat, move file to `InProcess/`.

### Drive Folder Structure
```
<RootFolder>/
  New/        ← worker polls here
  InProcess/  ← file moved here immediately after notification
  Done/       ← file moved here after ticket is closed
```

### Full Trigger Flow
1. Worker polls `New/` on a configurable interval
2. For each new file:
   - Read file content (text extraction — see Stage 10)
   - Generate a one-sentence description via LLM
   - Create `ErrorTicket` (`Source="GoogleDrive"`, `SourceFileId`, `State=New`)
   - Call Google Chat REST API → post **card message** to the space:
     title = filename, subtitle = short description, button = **Analyze**
   - Call Google Chat REST API → post **thread reply** with full file content
   - Save `MessageName` + `ThreadName` on the ticket
   - Move file from `New/` to `InProcess/`
3. Ticket stays `New` until the user clicks **Analyze**

### Domain Change
| Field | Type | Purpose |
|---|---|---|
| `ErrorTicket.ThreadName` | `string?` | Google Chat thread (`spaces/xxx/threads/zzz`) for all future replies |

### Files
| File | Purpose |
|---|---|
| `Worker/GoogleDriveWatcherWorker.cs` | Replaces default `Worker.cs`; polls on interval |
| `Infrastructure/Google/GoogleDriveService.cs` | List files in folder, read text, move file between folders |
| `Infrastructure/Google/IGoogleDriveService.cs` | Interface for Drive operations |
| `Infrastructure/Google/IGoogleChatApiService.cs` | Interface: proactive post message + thread reply |
| `Infrastructure/Google/GoogleChatApiService.cs` | Calls Google Chat REST API with service-account auth |
| `Infrastructure/Google/GoogleCredentialOptions.cs` | `ServiceAccountJson`, folder IDs, `ChatSpaceName`, polling interval |

**NuGet:** `Google.Apis.Drive.v3` (brings `Google.Apis.Auth` transitively)

### Result
When a file is dropped in `New/`, the team sees a Chat card with description + Analyze button; file automatically moves to `InProcess/`.

---

## Stage 10 — Google Drive File Reading

**Goal:** Extract readable text from files stored in Google Drive (Google Docs, plain text, images).

### Supported Formats
| MIME type | Strategy |
|---|---|
| `text/plain` | Direct download via Drive export |
| `application/vnd.google-apps.document` | Export as `text/plain` |
| `image/png` / `image/jpeg` | Download binary → OpenAI Vision API for text extraction |

### Files
| File | Purpose |
|---|---|
| `Infrastructure/Google/IDriveFileReader.cs` | Interface: `Task<string> ReadAsync(string fileId)` |
| `Infrastructure/Google/DriveFileReader.cs` | Dispatches by MIME type; falls back to filename + "image attached" |

### Result
Worker receives a plain-text string regardless of file type and stores it in `ErrorTicket.Description` / thread content.

---

## Stage 11 — Repository Analysis

**Goal:** On Analyze button click, AI reads the GitHub repository and posts its findings as a thread reply.

### Trigger
`CARD_CLICKED` → `actionMethodName = "analyze"` → `ActionController` → ticket `New → Analyzing`

### Analysis Flow
1. `ActionController` starts analysis for the ticket
2. `AgentService` runs with GitHub tools in scope:
   - `GitHubRepoTool` — reads file content by path
   - `GitHubSearchTool` — code search for relevant symbols/patterns
3. LLM produces an analysis report
4. Report saved as `ErrorTicket.AnalysisResult`
5. `GoogleChatApiService.PostThreadReplyAsync(threadName, report)` — posts report in bug thread
6. Ticket transitions `Analyzing → Analyzed`; card updated with **Fix** button

### Files
| File | Purpose |
|---|---|
| `Tools/Git/GitHubRepoTool.cs` | `ITool`: reads file content by path and ref |
| `Tools/Git/GitHubSearchTool.cs` | `ITool`: code search within repo |
| `Infrastructure/GitHub/GitHubClientFactory.cs` | Creates authenticated `GitHubClient` |
| `Infrastructure/GitHub/GitHubOptions.cs` | `Owner`, `Repo`, `Token` |

**NuGet:** `Octokit`

### Result
LLM analyses the codebase in context and posts findings directly in the bug-report thread.

---

## Stage 12 — Fix Pipeline (Branch / Commit / PR)

**Goal:** On Fix button click, AI creates a branch, commits the generated patch, opens a PR, and posts the link in the thread.

### Trigger
`CARD_CLICKED` → `actionMethodName = "fix"` → ticket `Analyzed → Fixing`

### Fix Flow
1. `AgentService` runs with fix tools: reads files, generates patch, commits to new branch, opens PR
2. `ErrorTicket.BranchName` + `ErrorTicket.PullRequestUrl` are saved
3. `GoogleChatApiService.PostThreadReplyAsync(threadName, prLink)` — PR link posted in thread
4. Ticket transitions `Fixing → Fixed`; card updated with **Close** button

### Files
| File | Purpose |
|---|---|
| `Tools/Git/CreateBranchTool.cs` | Creates a branch from a base ref |
| `Tools/Git/CommitFileTool.cs` | Creates or updates a file on a branch |
| `Tools/Git/CreatePullRequestTool.cs` | Opens PR with title/body |
| `Infrastructure/GitHub/GitHubService.cs` | Wraps Octokit for branch/commit/PR operations |

### Result
Full automated fix pipeline; PR link visible in the bug-report thread.

---

## Stage 13 — Human-in-the-Loop

**Goal:** Before opening a PR, bot asks for human approval inside the thread; pipeline proceeds only on Approve.

### Approval Flow
1. Bot posts thread reply: "Ready to open PR — **Approve** / **Reject**" (card with two buttons)
2. User clicks **Approve** → `CARD_CLICKED` → `ActionController` resolves the pending approval → PR is opened
3. User clicks **Reject** → ticket transitions back to `Analyzed`; user can adjust and retry
4. PR link posted in thread after approval

### Files
| File | Purpose |
|---|---|
| `Domain/Approvals/ApprovalRequest.cs` | `Id`, `TicketId`, `Description`, `State` |
| `Domain/Approvals/ApprovalState.cs` | Enum: `Pending`, `Approved`, `Rejected` |
| `Infrastructure/Approvals/InMemoryApprovalStore.cs` | `TaskCompletionSource<ApprovalState>` per request |
| `GoogleChatBot/Controllers/ActionController.cs` | Extended to resolve approvals via button callback |

### Result
Bot never opens a PR without explicit human approval in the thread.

---

## Status Summary

| Stage | Description | Status |
|---|---|---|
| 1 | Base Bot (Google Chat Webhook) | ✅ Done |
| 2 | Command System | ✅ Done |
| 3 | Tool Architecture | ✅ Done |
| 4 | LLM Integration | ✅ Done |
| 5 | LLM + Tools (Function Calling) | ✅ Done |
| 6 | MCP Server | ✅ Done |
| 6b | Swagger for GoogleChatBot | ✅ Done |
| 7 | Workflow + State Machine | ✅ Done |
| 8 | Google Chat Cards (Buttons) | ✅ Done |
| 9 | Google Drive Watcher + Chat Notification | ✅ Done |
| 9b | Persistent Ticket Repository (SQLite) | ✅ Done |
| 10 | Google Drive File Reading | ✅ Done |
| 11 | Repository Analysis | ✅ Done |
| 12 | Fix Pipeline (Branch / Commit — no PR) | ✅ Done |
| 13 | Human-in-the-Loop | 🚫 Superseded (no PR → no approval gate needed) |
