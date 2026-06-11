# AI Chat Bot — Implementation Plan

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

## Stage 9 — Worker (Polling)

**Goal:** Every 5 minutes, scan error sources and create `ErrorTickets`.

### Classes
| File | Purpose |
|---|---|
| `Worker/ErrorPollingWorker.cs` | Replaces default `Worker.cs`; runs every 5 min |
| `Worker/IErrorSource.cs` | `Task<IList<ErrorTicket>> FetchNewErrorsAsync(CancellationToken)` |
| `Worker/StubErrorSource.cs` | Returns deterministic fake errors for testing |

### Result
Worker automatically creates tickets and posts notifications to Google Chat.

---

## Stage 10 — Google Drive / Sheets Integration

**Goal:** Read real error data from a Google Sheets spreadsheet.

**NuGet:** `Google.Apis.Sheets.v4`, `Google.Apis.Drive.v3`

### Classes
| File | Purpose |
|---|---|
| `Infrastructure/Google/GoogleSheetsErrorSource.cs` | Implements `IErrorSource`; reads rows from configured sheet |
| `Infrastructure/Google/GoogleSheetsService.cs` | Low-level read/write wrapper |
| `Infrastructure/Google/GoogleDriveService.cs` | File metadata, change detection |
| `Infrastructure/Google/GoogleCredentialOptions.cs` | `ServiceAccountJson`, `SpreadsheetId`, `SheetRange` |

### Result
Worker uses a real Google Sheets file as error source.

---

## Stage 11 — Code Access (Repo Tools)

**Goal:** Read source files from GitHub so the LLM can analyse code.

**NuGet:** `Octokit`

### Classes
| File | Purpose |
|---|---|
| `Tools/Git/GitHubRepoTool.cs` | `ITool`: reads file content by path and ref |
| `Tools/Git/GitHubSearchTool.cs` | `ITool`: code search within repo |
| `Infrastructure/GitHub/GitHubClientFactory.cs` | Creates authenticated `GitHubClient` |
| `Infrastructure/GitHub/GitHubOptions.cs` | `Owner`, `Repo`, `Token` |

### Result
LLM can read source code files during analysis (fed into the `AgentService` loop).

---

## Stage 12 — Fix Pipeline (Branch / Commit / PR)

**Goal:** Create branch, apply LLM-generated fix, open Pull Request automatically.

### Classes
| File | Purpose |
|---|---|
| `Tools/Git/CreateBranchTool.cs` | Creates a branch from a base ref |
| `Tools/Git/CommitFileTool.cs` | Creates or updates a file on a branch |
| `Tools/Git/CreatePullRequestTool.cs` | Opens a PR with title/body |
| `Infrastructure/GitHub/GitHubService.cs` | Wraps Octokit for branch/commit/PR operations |

### Result
Full automated fix pipeline: LLM writes the fix, tools apply it, PR is opened.

---

## Stage 13 — Human-in-the-Loop

**Goal:** Pause the pipeline at critical steps and wait for human approval.

### Flow
1. Bot sends card: "Ready to open PR for fix `#abc` — Approve / Reject"
2. User clicks Approve
3. `ActionController` receives `CARD_CLICKED`, resolves pending approval
4. Pipeline resumes

### Classes
| File | Purpose |
|---|---|
| `Domain/Approvals/ApprovalRequest.cs` | `Id`, `TicketId`, `Description`, `State` |
| `Domain/Approvals/ApprovalState.cs` | Enum: `Pending`, `Approved`, `Rejected` |
| `Infrastructure/Approvals/InMemoryApprovalStore.cs` | `TaskCompletionSource` per request |
| `GoogleChatBot/Controllers/ActionController.cs` | Handles button callbacks; resolves approval |

### Result
Bot never opens a PR without explicit human approval.

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
| 9 | Worker (Polling) | 🔜 Next |
| 10 | Google Drive / Sheets Integration | ⬜ Planned |
| 11 | Code Access (Repo Tools) | ⬜ Planned |
| 12 | Fix Pipeline (Branch / Commit / PR) | ⬜ Planned |
| 13 | Human-in-the-Loop | ⬜ Planned |
