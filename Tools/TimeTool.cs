using Tools.Abstractions;

namespace Tools;

/// <summary>
/// Returns the current UTC date and time.
/// Input is ignored.
/// </summary>
public sealed class TimeTool : ITool
{
    public string Name        => "time";
    public string Description => "Returns the current UTC date and time";

    public string InputSchema => """
        {
          "type": "object",
          "properties": {}
        }
        """;

    public Task<string> ExecuteAsync(string input) =>
        Task.FromResult($"Current UTC time: *{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC*");
}
