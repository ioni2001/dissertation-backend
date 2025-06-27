using Models.LoggingModels;

namespace SignalRLogger;

public interface ISignalRLoggerService
{
    Task SendLogAsync(LogEntry logEntry);
}
