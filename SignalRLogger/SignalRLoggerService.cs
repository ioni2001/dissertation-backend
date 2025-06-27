using SignalRLogger.Hubs;
using Microsoft.AspNetCore.SignalR;
using Models.LoggingModels;

namespace SignalRLogger;

public class SignalRLoggerService : ISignalRLoggerService
{
    private readonly IHubContext<LoggingHub> _hubContext;

    public SignalRLoggerService(IHubContext<LoggingHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task SendLogAsync(LogEntry logEntry)
    {
        await _hubContext.Clients.Group("AllLogs").SendAsync("ReceiveLog", logEntry);
    }
}
