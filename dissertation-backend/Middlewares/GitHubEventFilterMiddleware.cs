using Models.GithubModels.WebhookModels;
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

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api/webhooks/github"))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("X-GitHub-Event", out var eventHeader))
        {
            _logger.LogWarning("Missing X-GitHub-Event header");

            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("Missing event type header");
            return;
        }

        var eventType = eventHeader.FirstOrDefault();
        if (eventType != "pull_request")
        {
            _logger.LogInformation("Ignoring non-pull_request event: {EventType}", eventType);

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

            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("Invalid JSON payload");
            return;
        }

        await _next(context);
    }
}
