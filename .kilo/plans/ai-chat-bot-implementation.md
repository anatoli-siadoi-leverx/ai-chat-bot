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

### Stage 9 — Worker (Polling)
**Goal:** Every 5 minutes, scan for new errors and create ErrorTickets.
**Files:**
- `Worker/ErrorPollingWorker.cs` — replaces default Worker.cs
- `Worker/IErrorSource.cs` — `Task<IList<ErrorTicket>> FetchNewErrorsAsync()`
- `Worker/StubErrorSource.cs` — returns fake errors for testing

**Result:** Worker creates ErrorTickets on schedule.

---

### Stage 10 — Google Drive / Sheets Integration
**Goal:** Read actual error data from Google Sheets.
**Packages:** `Google.Apis.Sheets.v4`, `Google.Apis.Drive.v3`
**Files:**
- `Infrastructure/Google/GoogleSheetsService.cs`
- `Infrastructure/Google/GoogleDriveService.cs`
- `Infrastructure/Google/GoogleCredentialOptions.cs` — ServiceAccountJson, SpreadsheetId

**Result:** Worker uses real Google Sheets as error source.

---

### Stage 11 — Code Access (Repo Tools)
**Goal:** Read files from a GitHub repository for LLM analysis.
**Package:** `Octokit`
**Files:**
- `Tools/Git/GitHubRepoTool.cs` — reads file content by path
- `Tools/Git/GitHubSearchTool.cs` — searches code
- `Infrastructure/GitHub/GitHubClientFactory.cs`
- `Infrastructure/GitHub/GitHubOptions.cs` — Owner, Repo, Token

**Result:** LLM can read source code during analysis.

---

### Stage 12 — Fix Pipeline (Branch / Commit / PR)
**Goal:** Create branch, apply fix, open Pull Request.
**Files:**
- `Tools/Git/CreateBranchTool.cs`
- `Tools/Git/CommitFileTool.cs`
- `Tools/Git/CreatePullRequestTool.cs`
- `Infrastructure/GitHub/GitHubService.cs` — wraps Octokit

**Result:** Full automated fix pipeline triggered by "Fix" button click.

---

### Stage 13 — Human-in-the-Loop
**Goal:** Pause pipeline at critical steps, wait for human approval via Google Chat buttons.
**Files:**
- `Domain/Approvals/ApprovalRequest.cs`
- `Domain/Approvals/ApprovalState.cs` — enum: Pending, Approved, Rejected
- `GoogleChatBot/Controllers/ActionController.cs` — handles button callbacks, resolves approvals
- `Infrastructure/Approvals/InMemoryApprovalStore.cs`

**Result:** Bot asks "Should I open this PR?" and only proceeds on Approve click.
