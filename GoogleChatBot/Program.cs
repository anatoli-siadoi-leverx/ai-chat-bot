using Domain.Repositories;
using Domain.Workflow;
using GoogleChatBot.Commands;
using GoogleChatBot.Controllers;
using GoogleChatBot.Handlers;
using Infrastructure.Google;
using Infrastructure.OpenAi;
using Infrastructure.Persistence;
using Tools;
using Tools.Abstractions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// ── Swagger ───────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title       = "Google Chat Bot",
        Version     = "v1",
        Description = "Webhook endpoint and ticket management for the AI Chat Bot."
    });

    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    options.IncludeXmlComments(xmlPath);
});

// ── LLM ───────────────────────────────────────────────────────────────────────
// API key: dotnet user-secrets set "OpenAi:ApiKey" "sk-..."
// Or set environment variable: OpenAi__ApiKey=sk-...
builder.Services.Configure<OpenAiOptions>(
    builder.Configuration.GetSection(OpenAiOptions.SectionName));

// Stage 5: AgentService wraps the OpenAI chat loop with tool-calling support.
// To revert to simple completion (no tools), swap back to OpenAiService.
builder.Services.AddSingleton<ILlmService, AgentService>();

// ── Tools ─────────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<HelloTool>();
builder.Services.AddSingleton<TimeTool>();

builder.Services.AddSingleton<ITool>(sp => sp.GetRequiredService<HelloTool>());
builder.Services.AddSingleton<ITool>(sp => sp.GetRequiredService<TimeTool>());

builder.Services.AddSingleton<ToolRegistry>();

// ── Stage 9b: Ticket Repository (SQLite — shared with Worker) ────────────────
var ticketsConn = builder.Configuration.GetConnectionString("Tickets")
    ?? "Data Source=tickets.db";
builder.Services.AddSqliteTickets(ticketsConn);
builder.Services.AddSingleton<TicketWorkflow>();

// ── Google Chat API (thread replies from ActionController) ───────────────────
builder.Services.Configure<GoogleCredentialOptions>(
    builder.Configuration.GetSection(GoogleCredentialOptions.SectionName));
builder.Services.AddSingleton<IGoogleChatApiService, GoogleChatApiService>();

// ── Commands ──────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<HelloCommand>(sp =>
    new HelloCommand(sp.GetRequiredService<HelloTool>()));

builder.Services.AddSingleton<TimeCommand>(sp =>
    new TimeCommand(sp.GetRequiredService<TimeTool>()));

builder.Services.AddSingleton<TicketCommand>(sp =>
    new TicketCommand(
        sp.GetRequiredService<ITicketRepository>(),
        sp.GetRequiredService<TicketWorkflow>()));

builder.Services.AddSingleton<StatusCommand>(sp =>
    new StatusCommand(sp.GetRequiredService<ITicketRepository>()));

builder.Services.AddSingleton<HelpCommand>(sp => new HelpCommand([
    sp.GetRequiredService<HelloCommand>(),
    sp.GetRequiredService<TimeCommand>(),
    sp.GetRequiredService<TicketCommand>(),
    sp.GetRequiredService<StatusCommand>(),
]));

builder.Services.AddSingleton<ICommand>(sp => sp.GetRequiredService<HelloCommand>());
builder.Services.AddSingleton<ICommand>(sp => sp.GetRequiredService<TimeCommand>());
builder.Services.AddSingleton<ICommand>(sp => sp.GetRequiredService<TicketCommand>());
builder.Services.AddSingleton<ICommand>(sp => sp.GetRequiredService<StatusCommand>());
builder.Services.AddSingleton<ICommand>(sp => sp.GetRequiredService<HelpCommand>());

builder.Services.AddSingleton<CommandDispatcher>();

// ── Stage 8: Card action handler ──────────────────────────────────────────────
builder.Services.AddSingleton<ActionController>();

// ── Event handler ─────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IChatEventHandler, ChatEventHandler>();

// ─────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

// Ensure SQLite schema exists and WAL mode is on
app.Services.EnsureDatabase();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Google Chat Bot v1");
    options.RoutePrefix = string.Empty;
});

app.MapControllers();

app.Run();
