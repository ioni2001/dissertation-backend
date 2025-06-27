using dissertation_backend.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Models.GithubModels.WebhookModels;
using Models.LoggingModels;
using SignalRLogger;

namespace dissertation_backend.Controllers;

[Route("api/webhooks")]
[ApiController]
public class WebhookController : ControllerBase
{
    private readonly IWebhookProcessingQueue _queue;
    private readonly ILogger<WebhookController> _logger;
    private readonly ISignalRLoggerService _signalRLoggerService;

    public WebhookController(
        IWebhookProcessingQueue queue,
        ILogger<WebhookController> logger,
        ISignalRLoggerService signalRLoggerService)
    {
        _queue = queue;
        _logger = logger;
        _signalRLoggerService = signalRLoggerService;
    }

    [HttpPost("github")]
    public async Task<IActionResult> HandleGitHubWebhookAsync()
    {
        try
        {
            await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Information, "Started Enqueueing Github Webhook event for procesing"));

            // Get the parsed payload from middleware
            var payload = HttpContext.Items["GitHubPayload"] as GitHubWebhookPayload;
            var eventType = HttpContext.Items["GitHubEventType"] as string;

            if (payload == null || string.IsNullOrEmpty(eventType))
            {
                _logger.LogWarning("Missing payload or event type from middleware");
                await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Warning, "Missing payload or event type from middleware"));

                return BadRequest("Invalid webhook data");
            }

            // Create processing item and enqueue
            var processingItem = new WebhookProcessingItem
            {
                EventType = eventType,
                Action = payload.Action,
                Payload = payload,
                ReceivedAt = DateTime.UtcNow
            };

            _queue.Enqueue(processingItem);

            _logger.LogInformation(
                "Enqueued webhook: EventType={EventType}, Action={Action}, PR={PrNumber}",
                eventType,
                payload.Action,
                payload.PullRequest?.Number);

            await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Information,
                $"Successfully Enqueued webhook: EventType={eventType}, Action={payload.Action}, PR={payload.PullRequest?.Number}"));

            return Ok(new { message = "Webhook received and queued for processing" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling GitHub webhook");
            await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Error, "Error handling GitHub webhook", ex.Message));

            return StatusCode(500, "Internal server error");
        }
    }

    private static LogEntry BuildLog(Models.LoggingModels.LogLevel logLevel, string message, string? exception = "")
    {
        return new LogEntry() { Level = logLevel, Message = message, Exception = exception, Component = "WebhookController" };
    }
}
