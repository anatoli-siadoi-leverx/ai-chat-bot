using Domain.Repositories;
using Infrastructure.Google;
using Infrastructure.OpenAi;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Tools;
using Tools.Abstractions;
using Worker;

var builder = Host.CreateApplicationBuilder(args);

// ── Google Drive + Chat ───────────────────────────────────────────────────────
builder.Services.Configure<GoogleCredentialOptions>(
    builder.Configuration.GetSection(GoogleCredentialOptions.SectionName));

builder.Services.AddSingleton<IGoogleDriveService, GoogleDriveService>();
builder.Services.AddSingleton<IGoogleChatApiService, GoogleChatApiService>();

// ── OpenAI (for LLM description generation) ──────────────────────────────────
// API key: dotnet user-secrets set "OpenAi:ApiKey" "sk-..."
builder.Services.Configure<OpenAiOptions>(
    builder.Configuration.GetSection(OpenAiOptions.SectionName));

// Worker uses the simple single-turn service (no tool-calling loop needed here)
builder.Services.AddSingleton<ILlmService, OpenAiService>();

// ── Tools (required by AgentService if switched later) ───────────────────────
builder.Services.AddSingleton<HelloTool>();
builder.Services.AddSingleton<TimeTool>();
builder.Services.AddSingleton<ITool>(sp => sp.GetRequiredService<HelloTool>());
builder.Services.AddSingleton<ITool>(sp => sp.GetRequiredService<TimeTool>());
builder.Services.AddSingleton<ToolRegistry>();

// ── Ticket repository (SQLite — shared with GoogleChatBot) ───────────────────
var ticketsConn = builder.Configuration.GetConnectionString("Tickets")
    ?? "Data Source=tickets.db";
builder.Services.AddSqliteTickets(ticketsConn);

// ── Hosted service ────────────────────────────────────────────────────────────
builder.Services.AddHostedService<GoogleDriveWatcherWorker>();

var host = builder.Build();

// Ensure SQLite schema exists and WAL mode is on
host.Services.EnsureDatabase();

host.Run();
