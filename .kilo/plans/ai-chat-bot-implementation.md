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

## Stage Reference — All 13 Stages

### Stage 1 — Base Bot (Google Chat Webhook)
**Goal:** Receive messages, return plain text.
**Files:**
- `GoogleChatBot/Controllers/ChatController.cs` — `[ApiController] [Route("chat")]`, `POST /chat`
- `GoogleChatBot/Models/Incoming/ChatEvent.cs` — `Type`, `Message`, `Space`, `Action`
- `GoogleChatBot/Models/Incoming/ChatMessage.cs` — `Name`, `Text`, `Sender`
- `GoogleChatBot/Models/Incoming/ChatSender.cs` — `Name`, `DisplayName`
- `GoogleChatBot/Models/Incoming/ChatSpace.cs` — `Name`, `Type` (`"DM"` | `"ROOM"`)
- `GoogleChatBot/Models/Incoming/ChatAction.cs` — `ActionMethodName`, `Parameters`
- `GoogleChatBot/Models/Incoming/ChatActionParameter.cs` — `Key`, `Value`
- `GoogleChatBot/Models/Outgoing/ChatResponse.cs` — `{ Text }` + `static From(string)`

All DTOs use `[JsonPropertyName]` for snake_case ↔ PascalCase mapping.

**Dispatch on `chatEvent.Type`:**
| Type | Action |
|---|---|
| `ADDED_TO_SPACE` | `ChatResponse.From("Hello! I'm your AI assistant…")` |
| `MESSAGE` | `CommandDispatcher` → on `null` result falls back to LLM |
| `CARD_CLICKED` | Delegates to `ActionController` |
| anything else | `Ok(new { })` |

**Result:** Working `POST /chat` webhook, verifiable via curl or ngrok.

---

### Stage 2 — Command System
**Goal:** Parse and dispatch user commands like `/help`, `/hello`, `/time`.
**Files:**
- `GoogleChatBot/Commands/ICommand.cs`
  ```csharp
  string Name { get; }
  string Description { get; }
  bool CanHandle(string input);
  Task<BotResponse> ExecuteAsync(string input);
  ```
- `GoogleChatBot/Commands/HelloCommand.cs` — `/hello [name]`, delegates to `HelloTool`
- `GoogleChatBot/Commands/TimeCommand.cs` — `/time`, delegates to `TimeTool`
- `GoogleChatBot/Commands/HelpCommand.cs` — `/help`, accepts `IReadOnlyList<ICommand>`, prepends itself at render time
- `GoogleChatBot/Commands/TicketCommand.cs` — `/ticket <description>`, creates ticket, returns card
- `GoogleChatBot/Commands/StatusCommand.cs` — `/status`, lists all tickets
- `GoogleChatBot/Commands/CommandDispatcher.cs` — accepts `IEnumerable<ICommand>`; returns `null` if input does not start with `/`

**Result:** Extensible command routing injected into `ChatController`.

---

### Stage 3 — Tool Architecture
**Goal:** Define tool abstractions in the `Tools` project.
**Files:**
- `Tools/Abstractions/ITool.cs`
  ```csharp
  string Name { get; }
  string Description { get; }
  string InputSchema { get; }   // raw JSON Schema string
  Task<string> ExecuteAsync(string input);
  ```
- `Tools/HelloTool.cs` — `name="hello"`, input → `"Hello, *{name}*!"`
- `Tools/TimeTool.cs` — `name="time"`, returns `DateTimeOffset.UtcNow` formatted as `yyyy-MM-dd HH:mm:ss UTC`
- `Tools/ToolRegistry.cs` — `ToolRegistry(IEnumerable<ITool>)`, `GetAll()`, `Find(name)` (case-insensitive)

**Note:** `IMcpTool` was not created — `InputSchema` is embedded directly in `ITool`. One interface serves all consumers.

**Result:** Tools project has working, testable tools with JSON Schema support.

---

### Stage 4 — LLM Integration
**Goal:** Call OpenAI Chat Completions from Infrastructure.
**Package:** `OpenAI 2.2.0` (official SDK)
**Files:**
- `Infrastructure/OpenAi/ILlmService.cs`
  ```csharp
  Task<string> CompleteAsync(string userMessage);
  ```
- `Infrastructure/OpenAi/OpenAiService.cs` — simple single-turn completion (fallback, not active)
- `Infrastructure/OpenAi/OpenAiOptions.cs` — config section `"OpenAi"`:
  `ApiKey`, `Model` (default `"gpt-4o-mini"`), `MaxTokens` (1024), `SystemPrompt`
- Secret stored via `dotnet user-secrets set "OpenAi:ApiKey" "sk-..."`

**Result:** Infrastructure can call OpenAI; the active service is replaced in Stage 5.

---

### Stage 5 — LLM + Tools (Function Calling)
**Goal:** Wire tools into the OpenAI function-calling loop.
**Files:**
- `Infrastructure/OpenAi/AgentService.cs` — implements `ILlmService`, active service registered in `GoogleChatBot`

**`AgentService.CompleteAsync` algorithm:**
1. Builds `[SystemChatMessage, UserChatMessage]`
2. Reads all tools from `ToolRegistry`, creates `ChatTool.CreateFunctionTool(name, description, BinaryData(inputSchema))` for each
3. Runs the loop (max `MaxIterations = 5`):
   - `Stop` → return text
   - `ToolCalls` → for each call: `Find(name)` → `ExtractFirstStringArg(json)` → `ExecuteAsync` → append `ToolChatMessage` → next iteration
4. `ExtractFirstStringArg` — parses the JSON object and returns the value of the first string property

**Result:** Bot answers "What time is it?" by calling `TimeTool` via LLM function calling.

---

### Stage 6 — MCP Server
**Goal:** Expose tools via Model Context Protocol HTTP API.
**Package:** `Swashbuckle.AspNetCore 10.2.1`
**Endpoint contract:**
- `GET  /mcp/tools`      → `{ tools: [ { name, description, inputSchema } ] }`
- `POST /mcp/tools/call` → `{ toolName, input }` → `{ result }` / 404 `{ error }`

**Files:**
- `McpServer/McpServer.csproj` — refs `Tools.csproj`, `Swashbuckle.AspNetCore`, XML doc enabled
- `McpServer/Program.cs` — controllers + tool DI + Swagger UI at root
- `McpServer/Controllers/McpController.cs` — `ListTools` + `CallTool` actions
- `McpServer/Models/McpDtos.cs` — `ToolInfo`, `ToolListResponse`, `ToolCallRequest`, `ToolCallResponse`

**Result:** McpServer is a standalone HTTP MCP endpoint with interactive Swagger UI.

---

### Stage 7 — Workflow + State Machine
**Goal:** ErrorTickets progress through defined states with guarded transitions.

**Allowed transitions:**
```
New → Analyzing → Analyzed → Fixing → Fixed → Closed
          ↘ Failed                 ↘ Failed
Failed → New  (retry)
```

**Files:**
- `Domain/Tickets/TicketState.cs` — enum: `New=0 … Closed=6`
- `Domain/Tickets/ErrorTicket.cs` — aggregate root
- `Domain/Repositories/ITicketRepository.cs` — `AddAsync`, `GetByIdAsync`, `GetAllAsync`, `UpdateAsync`
- `Domain/Workflow/IWorkflowTransition.cs` — `From` / `To` TicketState
- `Domain/Workflow/WorkflowException.cs` — thrown when transition is not permitted
- `Domain/Workflow/TicketWorkflow.cs` — static transition table; `CanTransition`, `GetAvailableTransitions`, `Transition`
- `Infrastructure/Repositories/InMemoryTicketRepository.cs` — `ConcurrentDictionary`-backed

**Result:** Tickets can be created, transitioned, and queried by state. `GetAvailableTransitions` feeds Stage 8 card buttons.

---

### Stage 8 — Google Chat Cards (Buttons)
**Goal:** Return rich cards with action buttons derived from workflow transitions.

**Files:**
- `GoogleChatBot/Models/Outgoing/BotResponse.cs` — discriminated union: `TextOnly` | `Card`
- `GoogleChatBot/Models/Outgoing/CardResponse.cs` — full cardsV2 JSON model
- `GoogleChatBot/Cards/ButtonAction.cs` — record: label + function name + parameters
- `GoogleChatBot/Cards/CardBuilder.cs` — fluent builder → `CardResponse`
- `GoogleChatBot/Cards/TicketCardBuilder.cs` — builds ticket card with buttons from `GetAvailableTransitions`
- `GoogleChatBot/Controllers/ActionController.cs` — handles `CARD_CLICKED`: applies transition, returns refreshed card

**Result:** Bot sends a card with ticket details + action buttons (Analyze, Fix, Close, etc.).

---

### Stage 9 — Google Drive Watcher + Chat Notification
**Goal:** Watch a Google Drive folder for new bug-report files; notify the team in Google Chat and move the file to InProcess.

**Drive folder structure:**
```
<RootFolder>/
  New/        ← worker watches here (txt, Google Docs, images)
  InProcess/  ← file moved here as soon as notification is sent
  Done/       ← file moved here after ticket is closed
```

**Full trigger flow:**
1. Worker polls `New/` every N minutes (configurable)
2. For each new file:
   a. Read file content (text extraction — see Stage 10)
   b. Generate a short description via LLM (one sentence)
   c. Create `ErrorTicket` (Source="GoogleDrive", SourceFileId, State=New)
   d. Call Google Chat REST API → post card message to the space:
      title = filename, subtitle = short description, button = "Analyze"
   e. Call Google Chat REST API → post thread reply with full file content
   f. Save `MessageName` + `ThreadName` on the ticket
   g. Move file from `New/` to `InProcess/`
3. Ticket remains in state `New` until user clicks "Analyze"

**Domain change:**
- `ErrorTicket.ThreadName` — Google Chat thread name for subsequent replies (e.g. `spaces/xxx/threads/zzz`)

**Files:**
- `Worker/GoogleDriveWatcherWorker.cs` — replaces default `Worker.cs`; polls on interval
- `Infrastructure/Google/GoogleDriveService.cs` — list files in folder, move file between folders
- `Infrastructure/Google/GoogleChatApiService.cs` — proactive messaging: post message, post thread reply
- `Infrastructure/Google/IGoogleChatApiService.cs` — interface for testability
- `Infrastructure/Google/GoogleCredentialOptions.cs` — `ServiceAccountJson`, `DriveFolderId`, `ChatSpaceName`

**Packages:** `Google.Apis.Drive.v3`, `Google.Apis.HangoutsChat.v1` (or direct REST with service-account JWT)

**Result:** When a file is dropped in `New/`, the team sees a Chat card with a description + Analyze button; file moves to `InProcess` automatically.

---

### Stage 10 — Google Drive File Reading
**Goal:** Read content from different file types stored in Google Drive.

**Supported formats:**
| MIME type | Strategy |
|---|---|
| `text/plain` | Direct read via Drive export |
| `application/vnd.google-apps.document` | Export as `text/plain` |
| `image/png`, `image/jpeg` | Download binary; pass to OpenAI Vision for text extraction |

**Files:**
- `Infrastructure/Google/DriveFileReader.cs` — reads a file by ID, returns `string` content
- `Infrastructure/Google/IDriveFileReader.cs` — interface

**Note:** Image handling (Vision API) added here; if unavailable, fallback to filename + "image attached".

**Result:** Worker can extract text from any supported file type and include it in the ticket description and Chat thread.

---

### Stage 11 — Repository Analysis
**Goal:** When user clicks "Analyze", the AI reads the target GitHub repository and posts its findings as a thread reply.

**Trigger:** `CARD_CLICKED` → `ActionMethodName = "analyze"` → `ActionController` → transitions ticket `New → Analyzing`

**Analysis flow:**
1. `ActionController` starts analysis for the ticket
2. `AgentService` runs with GitHub tools available:
   - `GitHubRepoTool` — reads file content by path
   - `GitHubSearchTool` — searches code for relevant symbols / patterns
3. LLM produces an analysis report
4. Report is saved as `ErrorTicket.AnalysisResult`
5. `GoogleChatApiService` posts the report as a thread reply (thread from `ErrorTicket.ThreadName`)
6. Ticket transitions `Analyzing → Analyzed`; card in Chat is updated with "Fix" button

**Files:**
- `Tools/Git/GitHubRepoTool.cs` — `ITool`: reads file content by path and ref
- `Tools/Git/GitHubSearchTool.cs` — `ITool`: code search within repo
- `Infrastructure/GitHub/GitHubClientFactory.cs` — creates authenticated `GitHubClient`
- `Infrastructure/GitHub/GitHubOptions.cs` — `Owner`, `Repo`, `Token`

**Package:** `Octokit`

**Result:** LLM analyses the codebase in context and posts its findings directly in the bug-report thread.

---

### Stage 12 — Fix Pipeline (Branch / Commit / PR)
**Goal:** When user clicks "Fix", the AI creates a branch, commits the fix, and opens a Pull Request; the PR link is posted as a thread reply.

**Trigger:** `CARD_CLICKED` → `ActionMethodName = "fix"` → ticket `Analyzed → Fixing`

**Fix flow:**
1. `AgentService` runs with fix tools: reads code, generates patch, commits to new branch, opens PR
2. `ErrorTicket.BranchName` and `ErrorTicket.PullRequestUrl` are saved
3. `GoogleChatApiService` posts PR link as thread reply
4. Ticket transitions `Fixing → Fixed`; card updated with "Close" button

**Files:**
- `Tools/Git/CreateBranchTool.cs` — creates branch from base ref
- `Tools/Git/CommitFileTool.cs` — creates or updates a file on a branch
- `Tools/Git/CreatePullRequestTool.cs` — opens PR with title/body
- `Infrastructure/GitHub/GitHubService.cs` — wraps Octokit for branch/commit/PR operations

**Result:** Full automated fix pipeline; PR link visible in the bug-report thread.

---

### Stage 13 — Human-in-the-Loop
**Goal:** Before opening a PR the bot asks for human approval in the thread; pipeline resumes only on explicit Approve.

**Approval flow:**
1. Bot posts thread reply: "Ready to open PR — Approve / Reject" (card with two buttons)
2. User clicks Approve → `CARD_CLICKED` → `ActionController` resolves the pending approval
3. PR is opened; link posted in thread
4. On Reject → ticket transitions back to `Analyzed`; user can adjust and retry

**Files:**
- `Domain/Approvals/ApprovalRequest.cs` — `Id`, `TicketId`, `Description`, `State`
- `Domain/Approvals/ApprovalState.cs` — enum: `Pending`, `Approved`, `Rejected`
- `Infrastructure/Approvals/InMemoryApprovalStore.cs` — `TaskCompletionSource<ApprovalState>` per request
- `GoogleChatBot/Controllers/ActionController.cs` — extended to resolve approvals

**Result:** Bot never opens a PR without explicit human approval in the thread.
