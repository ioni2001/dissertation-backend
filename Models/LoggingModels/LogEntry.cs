namespace Models.LoggingModels;

public class LogEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public LogLevel Level { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Component { get; set; } = string.Empty;
    public string? Exception { get; set; }
    public string Source { get; set; } = "Dissertation Backend";
}
