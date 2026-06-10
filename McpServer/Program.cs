using Tools;
using Tools.Abstractions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// ── Tools ─────────────────────────────────────────────────────────────────────
// Same double-registration pattern as GoogleChatBot:
// concrete type first (so tools can be resolved directly if needed),
// then as ITool so ToolRegistry can collect all of them via IEnumerable<ITool>.
builder.Services.AddSingleton<HelloTool>();
builder.Services.AddSingleton<TimeTool>();

builder.Services.AddSingleton<ITool>(sp => sp.GetRequiredService<HelloTool>());
builder.Services.AddSingleton<ITool>(sp => sp.GetRequiredService<TimeTool>());

builder.Services.AddSingleton<ToolRegistry>();

// ─────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

app.MapControllers();

app.Run();
