using GoogleChatBot.Commands;
using Infrastructure.OpenAi;
using Tools;
using Tools.Abstractions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// ── LLM ───────────────────────────────────────────────────────────────────────
// API key: dotnet user-secrets set "OpenAi:ApiKey" "sk-..."
// Or set environment variable: OpenAi__ApiKey=sk-...
builder.Services.Configure<OpenAiOptions>(
    builder.Configuration.GetSection(OpenAiOptions.SectionName));

builder.Services.AddSingleton<ILlmService, OpenAiService>();

// ── Tools ─────────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<HelloTool>();
builder.Services.AddSingleton<TimeTool>();

builder.Services.AddSingleton<ITool>(sp => sp.GetRequiredService<HelloTool>());
builder.Services.AddSingleton<ITool>(sp => sp.GetRequiredService<TimeTool>());

builder.Services.AddSingleton<ToolRegistry>();

// ── Commands ──────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<HelloCommand>(sp =>
    new HelloCommand(sp.GetRequiredService<HelloTool>()));

builder.Services.AddSingleton<TimeCommand>(sp =>
    new TimeCommand(sp.GetRequiredService<TimeTool>()));

builder.Services.AddSingleton<HelpCommand>(sp => new HelpCommand([
    sp.GetRequiredService<HelloCommand>(),
    sp.GetRequiredService<TimeCommand>(),
]));

builder.Services.AddSingleton<ICommand>(sp => sp.GetRequiredService<HelloCommand>());
builder.Services.AddSingleton<ICommand>(sp => sp.GetRequiredService<TimeCommand>());
builder.Services.AddSingleton<ICommand>(sp => sp.GetRequiredService<HelpCommand>());

builder.Services.AddSingleton<CommandDispatcher>();

// ─────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

app.MapControllers();

app.Run();
