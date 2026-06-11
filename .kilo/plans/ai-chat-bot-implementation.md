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

## Task A — Create IMPLEMENTATION_PLAN.md ✅ DONE

`IMPLEMENTATION_PLAN.md` created at the repository root.

---

## Task B — Domain Models ✅ DONE

`Domain/Class1.cs` deleted.

`Domain/Tickets/TicketState.cs` — enum: `New=0, Analyzing=1, Analyzed=2, Fixing=3, Fixed=4, Failed=5, Closed=6`

`Domain/Tickets/ErrorTicket.cs` — aggregate with fields:
`Id` (Guid), `Title`, `Description`, `Source`, `SourceFileId?`, `SourceRange?`, `SpaceName?`, `MessageName?`,
`State` (TicketState), `CreatedAt`, `UpdatedAt`, `AnalysisResult?`, `BranchName?`, `PullRequestUrl?`

---

## Task C — Stage 1–6 ✅ DONE

All changes are described in the **Stage Reference** section below.

---

## Stage Reference — All 13 Stages

### Stage 1 — Base Bot (Google Chat Webhook) ✅ DONE
**Goal:** Receive messages, return plain text.
**Files:**
- `GoogleChatBot/Controllers/ChatController.cs` — `[ApiController] [Route("chat")]`, `POST /chat`
- `GoogleChatBot/Models/Incoming/ChatEvent.cs` — `Type`, `Message`, `Space`, `Action`
- `GoogleChatBot/Models/Incoming/ChatMessage.cs` — `Name`, `Text`, `Sender`
- `GoogleChatBot/Models/Incoming/ChatSender.cs` — `Name`, `DisplayName`
- `GoogleChatBot/Models/Incoming/ChatSpace.cs` — `Name`, `Type` (`"DM"` | `"ROOM"`)
- `GoogleChatBot/Models/Incoming/ChatAction.cs` — `ActionMethodName`, `Parameters` *(added alongside Stage 2)*
- `GoogleChatBot/Models/Incoming/ChatActionParameter.cs` — `Key`, `Value`
- `GoogleChatBot/Models/Outgoing/ChatResponse.cs` — `{ Text }` + `static From(string)`

All DTOs use `[JsonPropertyName]` for snake_case ↔ PascalCase mapping.

**Dispatch on `chatEvent.Type`:**
| Type | Action |
|---|---|
| `ADDED_TO_SPACE` | `ChatResponse.From("Hello! I'm your AI assistant…")` |
| `MESSAGE` | `CommandDispatcher` → on `null` result falls back to LLM |
| `CARD_CLICKED` | Placeholder (logs `ActionMethodName`) |
| anything else | `Ok(new { })` |

**Result:** Working `POST /chat` webhook, verifiable via curl or ngrok.

---

### Stage 2 — Command System ✅ DONE
**Goal:** Parse and dispatch user commands like `/help`, `/hello`, `/time`.
**Files:**
- `GoogleChatBot/Commands/ICommand.cs`
  ```csharp
  string Name { get; }
  string Description { get; }
  bool CanHandle(string input);
  Task<string> ExecuteAsync(string input);   // string-in / string-out
  ```
- `GoogleChatBot/Commands/HelloCommand.cs` — `/hello [name]`, delegates to `HelloTool`
- `GoogleChatBot/Commands/TimeCommand.cs` — `/time`, delegates to `TimeTool`
- `GoogleChatBot/Commands/HelpCommand.cs` — `/help`, accepts `IReadOnlyList<ICommand>`, prepends itself at render time to avoid a circular dependency
- `GoogleChatBot/Commands/CommandDispatcher.cs` — accepts `IEnumerable<ICommand>`; returns `null` if input does not start with `/`, otherwise finds a matching `CanHandle` or returns an "Unknown command" message

**Deviation from plan:** `ICommand.ExecuteAsync` accepts `string` instead of `ChatEvent` and returns `string` instead of `ChatResponse` — simplified to work with raw text.

**Commands not implemented from plan:** `StatusCommand`, `TicketCommand` — deferred.

**Result:** Extensible command routing injected into `ChatController`.

---

### Stage 3 — Tool Architecture ✅ DONE
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

**Deviation from plan:** A separate `IMcpTool` was not created — `InputSchema` is embedded directly in `ITool`. A single interface serves all consumers (commands, LLM, MCP).

**Result:** Tools project has working, testable tools with JSON Schema support.

---

### Stage 4 — LLM Integration ✅ DONE
**Goal:** Call OpenAI Chat Completions from Infrastructure.
**Package:** `OpenAI 2.2.0` (official SDK)
**Files:**
- `Infrastructure/OpenAi/ILlmService.cs`
  ```csharp
  Task<string> CompleteAsync(string userMessage);
  ```
- `Infrastructure/OpenAi/OpenAiService.cs` — `ILlmService`, simple single-turn completion: `[system, user] → text`. **Exists as a fallback**, not registered in `GoogleChatBot`.
- `Infrastructure/OpenAi/OpenAiOptions.cs` — config section `"OpenAi"`:
  `ApiKey`, `Model` (default `"gpt-4o-mini"`), `MaxTokens` (1024), `SystemPrompt`
- Secret stored via `dotnet user-secrets set "OpenAi:ApiKey" "sk-..."`

**Deviation from plan:** `ILlmService.CompleteAsync` accepts `string` instead of `IList<LlmMessage>` — the system prompt lives in `OpenAiOptions`, not in the call site. `LlmMessage.cs` was not created.

**Result:** Infrastructure can call OpenAI; the active service is replaced in Stage 5.

---

### Stage 5 — LLM + Tools (Function Calling) ✅ DONE
**Goal:** Wire tools into the OpenAI function-calling loop.
**Files:**
- `Infrastructure/OpenAi/AgentService.cs` — implements `ILlmService`, **active** service registered in `GoogleChatBot`

**`AgentService.CompleteAsync` algorithm:**
1. Builds `[SystemChatMessage, UserChatMessage]`
2. Reads all tools from `ToolRegistry`, creates `ChatTool.CreateFunctionTool(name, description, BinaryData(inputSchema))` for each
3. Runs the loop (max `MaxIterations = 5`):
   - `Stop` → return text
   - `ToolCalls` → for each call: `Find(name)` → `ExtractFirstStringArg(json)` → `ExecuteAsync` → append `ToolChatMessage` → next iteration
   - otherwise → return text or fallback string
4. `ExtractFirstStringArg` — parses the JSON object and returns the value of the first string property (or `""` for empty objects)

**DI registration (`GoogleChatBot/Program.cs`):**
```csharp
builder.Services.AddSingleton<ILlmService, AgentService>();  // Stage 5 active
```

**Deviation from plan:** `ToolFunctionMapper.cs` and `AgentLoop.cs` as separate files were not created — the entire agent loop is consolidated in `AgentService.cs`.

**Result:** Bot answers "What time is it?" by calling `TimeTool` via LLM function calling.

---

### Stage 6 — MCP Server ✅ DONE
**Goal:** Expose tools via Model Context Protocol HTTP API.
**Endpoint contract (implemented):**
- `GET  /mcp/tools`      → `{ tools: [ { name, description, inputSchema } ] }`
- `POST /mcp/tools/call` → `{ toolName, input }` → `{ result }` / 404 `{ error }`
**Swagger UI:** served at `/` (root) → `http://localhost:5295/`
**Package:** `Swashbuckle.AspNetCore 10.2.1`
**Files created:**
- `McpServer/McpServer.csproj` — refs `Tools.csproj`, `Swashbuckle.AspNetCore`, XML doc enabled
- `McpServer/Program.cs` — controllers + tool DI + Swagger + SwaggerUI at root
- `McpServer/Controllers/McpController.cs` — `ListTools` + `CallTool` actions
- `McpServer/Models/McpDtos.cs` — `ToolInfo`, `ToolListResponse`, `ToolCallRequest`, `ToolCallResponse`
**Result:** McpServer is a standalone HTTP MCP endpoint with interactive Swagger UI.

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

### ✅ Completed (Stages 1–6)
1. `Domain/Tickets/TicketState.cs` — enum
2. `Domain/Tickets/ErrorTicket.cs` — aggregate
3. `GoogleChatBot/Program.cs` — controllers, tools, commands, LLM DI
4. `GoogleChatBot/Models/Incoming/` — `ChatEvent`, `ChatMessage`, `ChatSender`, `ChatSpace`, `ChatAction`, `ChatActionParameter`
5. `GoogleChatBot/Models/Outgoing/ChatResponse.cs`
6. `GoogleChatBot/Controllers/ChatController.cs` — webhook with command + LLM dispatch
7. `GoogleChatBot/Commands/` — `ICommand`, `HelloCommand`, `TimeCommand`, `HelpCommand`, `CommandDispatcher`
8. `Tools/Abstractions/ITool.cs`
9. `Tools/HelloTool.cs`, `Tools/TimeTool.cs`, `Tools/ToolRegistry.cs`
10. `Infrastructure/OpenAi/ILlmService.cs`, `OpenAiOptions.cs`, `OpenAiService.cs`, `AgentService.cs`
11. `McpServer/Controllers/McpController.cs` — `GET /mcp/tools`, `POST /mcp/tools/call`
12. `McpServer/Models/McpDtos.cs` — `ToolInfo`, `ToolListResponse`, `ToolCallRequest`, `ToolCallResponse`
13. `McpServer/Program.cs` — controllers, tools, Swagger UI at `/`

### 🔜 Next: Stage 7 — Workflow + State Machine
