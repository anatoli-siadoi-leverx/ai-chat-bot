using Infrastructure.Google;
using Infrastructure.OpenAi;
using Infrastructure.Persistence;
using Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<GoogleCredentialOptions>(builder.Configuration.GetSection(GoogleCredentialOptions.SectionName));

builder.Services.AddSingleton<IGoogleDriveService, GoogleDriveService>();
builder.Services.AddSingleton<IDriveFileReader, DriveFileReader>();
builder.Services.AddSingleton<IGoogleChatApiService, GoogleChatApiService>();

builder.Services.Configure<OpenAiOptions>(builder.Configuration.GetSection(OpenAiOptions.SectionName));
builder.Services.AddSingleton<ILlmService, OpenAiService>();

var ticketsConn = builder.Configuration.GetConnectionString("Tickets")
    ?? "Data Source=tickets.db";
builder.Services.AddSqliteTickets(ticketsConn);

builder.Services.AddHostedService<GoogleDriveWatcherWorker>();

var host = builder.Build();

// Ensure SQLite schema exists and WAL mode is on
host.Services.EnsureDatabase();

host.Run();
