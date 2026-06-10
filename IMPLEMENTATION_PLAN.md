# AI Chat Bot — Implementation Plan

## Architecture Overview

**Solution:** `AiChatBotSolution.slnx` | **Target:** net10.0

### Project Dependency Graph

```
Domain          (no external deps)
   ↑
Tools         → Domain
   ↑
Infrastructure → Domain, Tools
   ↑
GoogleChatBot  → Domain, Infrastructure, Tools
McpServer      → Tools
Worker         → Domain, Infrastructure
```

---

## Stage 1 — Base Bot (Google Chat Webhook)

**Goal:** Receive Google Chat webhook, parse the message, return plain text.

### What is implemented
- `POST /chat` endpoint
- Deserialisation of all Google Chat event types
- Plain-text echo response

### Classes
| File | Purpose |
|---|---|
| `GoogleChatBot/Controllers/ChatController.cs` | Receives webhook, dispatches by event type |
| `GoogleChatBot/Models/Incoming/ChatEvent.cs` | Root envelope: `type`, `message`, `space` |
| `GoogleChatBot/Models/Incoming/ChatMessage.cs` | `name`, `text`, `sender` |
| `GoogleChatBot/Models/Incoming/ChatSender.cs` | `name`, `displayName` |
| `GoogleChatBot/Models/Incoming/ChatSpace.cs` | `name`, `type` |
| `GoogleChatBot/Models/Outgoing/ChatResponse.cs` | `{ "text": "..." }` |

### Result
Working webhook verifiable via curl / ngrok. Auth-verification skipped (added in a later stage).

---

## Stage 2 — Command System

**Goal:** Parse and dispatch slash commands: `/help`, `/status`, `/ticket <id>`.

### Classes
| File | Purpose |
|---|---|
| `GoogleChatBot/Commands/ICommand.cs` | `Task<ChatResponse> ExecuteAsync(ChatEvent)` |
| `GoogleChatBot/Commands/HelpCommand.cs` | Returns available commands list |
| `GoogleChatBot/Commands/StatusCommand.cs` | Returns system health |
| `GoogleChatBot/Commands/CommandDispatcher.cs` | Parses `/prefix`, routes to ICommand |

### Result
Extensible, DI-registered command routing. ChatController delegates to CommandDispatcher.

---

## Stage 3 — Tool Architecture

**Goal:** Define tool abstractions in `Tools`; implement HelloTool and TimeTool.

### Classes
| File | Purpose |
|---|---|
| `Tools/Abstractions/ITool.cs` | `Name`, `Description`, `Task<string> ExecuteAsync(string input)` |
| `Tools/Abstractions/IMcpTool.cs` | Extends ITool with `JsonElement InputSchema` |
| `Tools/HelloTool.cs` | Returns greeting with timestamp |
| `Tools/TimeTool.cs` | Returns current UTC time |
| `Tools/ToolRegistry.cs` | `IReadOnlyList<ITool> GetAll()` — DI-registered |

### Result
Tools project has independently testable tools with a shared registry.

---

## Stage 4 — LLM Integration

**Goal:** Call OpenAI Chat Completions API from `Infrastructure`.

**NuGet:** `OpenAI` (official .NET SDK)

### Classes
| File | Purpose |
|---|---|
| `Infrastructure/OpenAi/ILlmService.cs` | `Task<string> CompleteAsync(IList<LlmMessage> messages)` |
| `Infrastructure/OpenAi/OpenAiService.cs` | Implements ILlmService via OpenAI SDK |
| `Infrastructure/OpenAi/OpenAiOptions.cs` | `ApiKey`, `Model`, `MaxTokens` |
| `Infrastructure/OpenAi/LlmMessage.cs` | `Role` (enum), `Content` |

### Result
GoogleChatBot can ask LLM a question and return the AI-generated answer.

---

## Stage 5 — LLM + Tools (Function Calling)

**Goal:** Wire `IMcpTool` implementations into the OpenAI function-calling loop.

### Classes
| File | Purpose |
|---|---|
| `Infrastructure/OpenAi/ToolFunctionMapper.cs` | Converts `IMcpTool` → OpenAI `ChatTool` definition |
| `Infrastructure/OpenAi/AgentLoop.cs` | Iterates tool-call / tool-result rounds until final text |

### Result
Bot can answer "What time is it?" by having the LLM call `TimeTool` autonomously.

---

## Stage 6 — MCP Server

**Goal:** Expose tools via Model Context Protocol over HTTP.

### Endpoint Contract
```
POST /mcp/tools/list  → { tools: [{ name, description, inputSchema }] }
POST /mcp/tools/call  → { toolName, input }  →  { result }
```

### Classes
| File | Purpose |
|---|---|
| `McpServer/Controllers/McpController.cs` | Handles list + call |
| `McpServer/Models/ToolListResponse.cs` | Response envelope |
| `McpServer/Models/ToolCallRequest.cs` | `ToolName`, `Input` |
| `McpServer/Models/ToolCallResponse.cs` | `Result`, `IsError` |

### Result
McpServer is a standalone HTTP MCP endpoint consumable by any MCP client.

---

## Stage 7 — Workflow + State Machine

**Goal:** ErrorTickets progress through defined states with guarded transitions.

### Allowed Transitions
```
New → Analyzing → Analyzed → Fixing → Fixed → Closed
Any → Failed
Failed → Closed
```

### Classes
| File | Purpose |
|---|---|
| `Domain/Workflow/TicketWorkflow.cs` | Defines and validates transitions |
| `Domain/Workflow/WorkflowException.cs` | Thrown on invalid transition |
| `Domain/Repositories/ITicketRepository.cs` | CRUD interface |
| `Infrastructure/Repositories/InMemoryTicketRepository.cs` | In-memory implementation |

### Result
Tickets can be created, safely transitioned, and queried by state.

---

## Stage 8 — Google Chat Cards (Buttons)

**Goal:** Return rich interactive cards with action buttons (Analyze, Fix, Close).

### Google Chat Card V2 structure (simplified)
```json
{
  "cardsV2": [{
    "card": {
      "header": { "title": "Error #123" },
      "sections": [{
        "widgets": [
          { "textParagraph": { "text": "..." } },
          { "buttonList": { "buttons": [
            { "text": "Analyze", "onClick": { "action": { "function": "analyze", "parameters": [...] } } }
          ]}}
        ]
      }]
    }
  }]
}
```

### Classes
| File | Purpose |
|---|---|
| `GoogleChatBot/Cards/CardBuilder.cs` | Fluent builder for card V2 payload |
| `GoogleChatBot/Cards/ButtonAction.cs` | Value type: label + function name + parameters |
| `GoogleChatBot/Models/Outgoing/CardResponse.cs` | Serialises to `cardsV2` |
| `GoogleChatBot/Controllers/ActionController.cs` | Handles `CARD_CLICKED` events |

### Result
Bot sends cards with ticket info + Analyze / Fix / Close buttons.

---

## Stage 9 — Worker (Polling)

**Goal:** Every 5 minutes, scan error sources and create ErrorTickets.

### Classes
| File | Purpose |
|---|---|
| `Worker/ErrorPollingWorker.cs` | Replaces default Worker; runs every 5 min |
| `Worker/IErrorSource.cs` | `Task<IList<ErrorTicket>> FetchNewErrorsAsync(CancellationToken)` |
| `Worker/StubErrorSource.cs` | Returns deterministic fake errors (for testing) |

### Result
Worker automatically creates tickets and posts notifications to Google Chat.

---

## Stage 10 — Google Drive / Sheets Integration

**Goal:** Read real error data from a Google Sheets spreadsheet.

**NuGet:** `Google.Apis.Sheets.v4`, `Google.Apis.Drive.v3`

### Classes
| File | Purpose |
|---|---|
| `Infrastructure/Google/GoogleSheetsErrorSource.cs` | Implements IErrorSource; reads rows from configured sheet |
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
| `Tools/Git/GitHubRepoTool.cs` | `IMcpTool`: reads file content by path and ref |
| `Tools/Git/GitHubSearchTool.cs` | `IMcpTool`: code search within repo |
| `Infrastructure/GitHub/GitHubClientFactory.cs` | Creates authenticated `GitHubClient` |
| `Infrastructure/GitHub/GitHubOptions.cs` | `Owner`, `Repo`, `Token` |

### Result
LLM can read source code files during analysis (fed into the AgentLoop).

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
| `Infrastructure/Approvals/InMemoryApprovalStore.cs` | Holds pending approvals; `TaskCompletionSource` per request |
| `GoogleChatBot/Controllers/ActionController.cs` | Handles button callbacks; resolves approval |

### Result
Bot never opens a PR without explicit human approval.

---

## Auth & Security Roadmap

| Stage | What |
|---|---|
| Stage 1 | No auth (dev only) |
| Stage 2 | Google JWT Bearer verification middleware |
| Production | HTTPS, secrets in user-secrets / Azure Key Vault |
