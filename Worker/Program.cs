using Domain.Repositories;
using Infrastructure.Google;
using Infrastructure.OpenAi;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Worker;

var builder = Host.CreateApplicationBuilder(args);

// ── Google Drive + Chat ───────────────────────────────────────────────────────
builder.Services.Configure<GoogleCredentialOptions>(
    builder.Configuration.GetSection(GoogleCredentialOptions.SectionName));

builder.Services.AddSingleton<IGoogleDriveService, GoogleDriveService>();
builder.Services.AddSingleton<IDriveFileReader, DriveFileReader>();
builder.Services.AddSingleton<IGoogleChatApiService, GoogleChatApiService>();

// ── OpenAI (single-turn description generation — no tool-calling needed) ──────
builder.Services.Configure<OpenAiOptions>(
    builder.Configuration.GetSection(OpenAiOptions.SectionName));
builder.Services.AddSingleton<ILlmService, OpenAiService>();

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
