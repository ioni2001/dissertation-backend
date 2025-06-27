using Models.GithubModels.WebhookModels;
using Models.LoggingModels;
using SignalRLogger;
using System.Text.Json;

namespace dissertation_backend.Middlewares;

public class GitHubEventFilterMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GitHubEventFilterMiddleware> _logger;
    private static readonly string[] AllowedActions = { "opened", "synchronize" };

    public GitHubEventFilterMiddleware(
        RequestDelegate next,
        ILogger<GitHubEventFilterMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ISignalRLoggerService signalRLoggerService)
    {
        await signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Information, "Started GitHub Event filtering"));

        if (!context.Request.Path.StartsWithSegments("/api/webhooks/github"))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("X-GitHub-Event", out var eventHeader))
        {
            _logger.LogWarning("Missing X-GitHub-Event header");
            await signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Error, "Missing X-GitHub-Event header"));

            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("Missing event type header");
            return;
        }

        var eventType = eventHeader.FirstOrDefault();
        if (eventType != "pull_request")
        {
            _logger.LogInformation("Ignoring non-pull_request event: {EventType}", eventType);
            await signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Information, $"Ignoring non-pull_request event: {eventType}"));

            context.Response.StatusCode = 200;
            await context.Response.WriteAsync("Event ignored");
            return;
        }

        context.Request.EnableBuffering();
        var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
        context.Request.Body.Position = 0;

        try
        {
            var payload = JsonSerializer.Deserialize<GitHubWebhookPayload>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (payload == null || !AllowedActions.Contains(payload.Action))
            {
                _logger.LogInformation("Ignoring pull_request action: {Action}", payload?.Action);
                await signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Information, $"Ignoring pull_request action: {payload?.Action}"));

                context.Response.StatusCode = 200;
                await context.Response.WriteAsync("Action ignored");
                return;
            }

            context.Items["GitHubPayload"] = payload;
            context.Items["GitHubEventType"] = eventType;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse GitHub webhook payload");
            await signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Error, $"Failed to parse GitHub webhook payload", ex.Message));

            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("Invalid JSON payload");
            return;
        }

        await signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Information, "GitHub Event filtering completed successfully"));

        await _next(context);
    }

    private static LogEntry BuildLog(Models.LoggingModels.LogLevel logLevel, string message, string? exception = "")
    {
        return new LogEntry() { Level = logLevel, Message = message, Exception = exception, Component = "GitHubEventFilterMiddleware" };
    }
}
