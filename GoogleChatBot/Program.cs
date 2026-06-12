using Domain.Repositories;
using Domain.Workflow;
using GitHubTools;
using GoogleChatBot.Commands;
using GoogleChatBot.Controllers;
using GoogleChatBot.Handlers;
using GoogleChatBot.Services;
using Infrastructure.Analysis;
using Infrastructure.Fix;
using Infrastructure.GitHub;
using Infrastructure.Google;
using Infrastructure.OpenAi;
using Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using Tools;
using Tools.Abstractions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "Google Chat Bot",
        Version = "v1",
        Description = "Webhook endpoint and ticket management for the AI Chat Bot."
    });

    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    options.IncludeXmlComments(xmlPath);
});

builder.Services.Configure<OpenAiOptions>(
    builder.Configuration.GetSection(OpenAiOptions.SectionName));

builder.Services.AddSingleton<ILlmService, AgentService>();

builder.Services.AddSingleton<ToolRegistry>();

var ticketsConn = builder.Configuration.GetConnectionString("Tickets")
    ?? "Data Source=tickets.db";
builder.Services.AddSqliteTickets(ticketsConn);
builder.Services.AddSingleton<TicketWorkflow>();

builder.Services.Configure<GoogleCredentialOptions>(
    builder.Configuration.GetSection(GoogleCredentialOptions.SectionName));
builder.Services.AddSingleton<IGoogleChatApiService, GoogleChatApiService>();
builder.Services.AddSingleton<IGoogleDriveService, GoogleDriveService>();

builder.Services.Configure<GitHubOptions>(
    builder.Configuration.GetSection(GitHubOptions.SectionName));
builder.Services.AddSingleton<IGitHubService, GitHubService>();

// GitHub tools — registered as keyed ITool so AnalysisService + FixService can
// request them via [FromKeyedServices].  Intentionally NOT registered as plain
// ITool so ToolRegistry (general chat) stays empty.
builder.Services.AddKeyedSingleton<ITool, GitHubRepoTool>("github_read_file");
builder.Services.AddKeyedSingleton<ITool, GitHubSearchTool>("github_search_code");
builder.Services.AddKeyedSingleton<ITool, CommitFileTool>("github_commit_file");

builder.Services.AddSingleton<IAnalysisService, AnalysisService>();
builder.Services.AddSingleton<IFixService, FixService>();

// ── Ticket background-task abstractions ───────────────────────────────────────
builder.Services.AddSingleton<ITicketThreadNotifier, TicketThreadNotifier>();
builder.Services.AddSingleton<ITicketPipelineRunner, TicketPipelineRunner>();

builder.Services.AddSingleton<TicketCommand>(sp =>
    new TicketCommand(
        sp.GetRequiredService<ITicketRepository>(),
        sp.GetRequiredService<TicketWorkflow>()));

builder.Services.AddSingleton<StatusCommand>(sp =>
    new StatusCommand(sp.GetRequiredService<ITicketRepository>()));

builder.Services.AddSingleton<CleanCommand>(sp =>
    new CleanCommand(sp.GetRequiredService<ITicketRepository>()));

builder.Services.AddSingleton<ReportCommand>(sp =>
    new ReportCommand(
        sp.GetRequiredService<IGoogleDriveService>(),
        sp.GetRequiredService<IOptions<GoogleCredentialOptions>>()));

builder.Services.AddSingleton<HelpCommand>(sp => new HelpCommand([
    sp.GetRequiredService<TicketCommand>(),
    sp.GetRequiredService<StatusCommand>(),
    sp.GetRequiredService<CleanCommand>(),
    sp.GetRequiredService<ReportCommand>(),
]));

builder.Services.AddSingleton<ICommand>(sp => sp.GetRequiredService<TicketCommand>());
builder.Services.AddSingleton<ICommand>(sp => sp.GetRequiredService<StatusCommand>());
builder.Services.AddSingleton<ICommand>(sp => sp.GetRequiredService<CleanCommand>());
builder.Services.AddSingleton<ICommand>(sp => sp.GetRequiredService<ReportCommand>());
builder.Services.AddSingleton<ICommand>(sp => sp.GetRequiredService<HelpCommand>());

builder.Services.AddSingleton<CommandDispatcher>();

// ── Card action handler ───────────────────────────────────────────────────────
builder.Services.AddSingleton<ActionController>();

builder.Services.AddSingleton<IChatEventHandler, ChatEventHandler>();

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
