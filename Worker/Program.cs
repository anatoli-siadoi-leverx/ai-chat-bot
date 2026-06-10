using Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<DefaultWorker>();

var host = builder.Build();
host.Run();
