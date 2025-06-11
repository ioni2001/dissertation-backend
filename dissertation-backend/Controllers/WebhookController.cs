using dissertation_backend.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Models.GithubModels;
using Models.GithubModels.WebhookModels;

namespace dissertation_backend.Controllers;

[Route("api/webhooks")]
[ApiController]
public class WebhookController : ControllerBase
{
    private readonly IWebhookProcessingQueue _queue;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(
        IWebhookProcessingQueue queue,
        ILogger<WebhookController> logger)
    {
        _queue = queue;
        _logger = logger;
    }

    [HttpPost("github")]
    public IActionResult HandleGitHubWebhook()
    {
        try
        {
            // Get the parsed payload from middleware
            var payload = HttpContext.Items["GitHubPayload"] as GitHubWebhookPayload;
            var eventType = HttpContext.Items["GitHubEventType"] as string;

            if (payload == null || string.IsNullOrEmpty(eventType))
            {
                _logger.LogWarning("Missing payload or event type from middleware");

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

            return Ok(new { message = "Webhook received and queued for processing" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling GitHub webhook");

            return StatusCode(500, "Internal server error");
        }
    }
}
