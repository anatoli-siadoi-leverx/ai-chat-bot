using GoogleChatBot.Commands;
using Tools;
using Tools.Abstractions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// ── Tools ─────────────────────────────────────────────────────────────────────
// Register each tool as its concrete type first (for injection into commands),
// then forward as ITool (for ToolRegistry).

builder.Services.AddSingleton<HelloTool>();
builder.Services.AddSingleton<TimeTool>();

builder.Services.AddSingleton<ITool>(sp => sp.GetRequiredService<HelloTool>());
builder.Services.AddSingleton<ITool>(sp => sp.GetRequiredService<TimeTool>());

builder.Services.AddSingleton<ToolRegistry>();

// ── Commands ──────────────────────────────────────────────────────────────────
// Commands are registered as concrete types (for HelpCommand's peer list),
// then forwarded as ICommand (for CommandDispatcher).
// HelpCommand avoids circular DI by receiving an explicit peer list.

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
