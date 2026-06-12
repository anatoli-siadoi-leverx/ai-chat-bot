using GitHubTools;
using Infrastructure.GitHub;
using Infrastructure.OpenAi;
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
        Title       = "MCP Server",
        Version     = "v1",
        Description = "HTTP API for listing and calling AI tools via the Model Context Protocol."
    });

    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    options.IncludeXmlComments(xmlPath);
});

// ── GitHub services ───────────────────────────────────────────────────────────
builder.Services.Configure<GitHubOptions>(
    builder.Configuration.GetSection(GitHubOptions.SectionName));
builder.Services.AddSingleton<IGitHubService, GitHubService>();

// ── GitHub tools — registered as plain ITool for ToolRegistry ────────────────
// McpServer exposes these to external MCP clients via GET /mcp/tools.
builder.Services.AddSingleton<ITool, GitHubRepoTool>();
builder.Services.AddSingleton<ITool, GitHubSearchTool>();
builder.Services.AddSingleton<ITool, CommitFileTool>();

builder.Services.AddSingleton<ToolRegistry>();

// ── OpenAI (only needed if McpServer hosts an agent in the future) ────────────
builder.Services.Configure<OpenAiOptions>(
    builder.Configuration.GetSection(OpenAiOptions.SectionName));

// ─────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "MCP Server v1");
    options.RoutePrefix = string.Empty;
});

app.MapControllers();

app.Run();
