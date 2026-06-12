namespace Worker;

/// <summary>
/// Placeholder background service. Will be replaced by ErrorPollingWorker in Stage 9.
/// </summary>
public class DefaultWorker(ILogger<DefaultWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
