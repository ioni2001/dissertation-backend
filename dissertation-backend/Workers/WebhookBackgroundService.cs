using dissertation_backend.Services.Interfaces;
using Models.GithubModels;

namespace dissertation_backend.Workers;

public class WebhookBackgroundService : BackgroundService
{
    private readonly IWebhookProcessingQueue _queue;
    private readonly ILogger<WebhookBackgroundService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public WebhookBackgroundService(
        IWebhookProcessingQueue queue,
        ILogger<WebhookBackgroundService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _queue = queue;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Webhook background service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var item = await _queue.DequeueAsync(stoppingToken);
                if (item != null)
                {
                    await ProcessWebhookAsync(item);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing webhook item");
            }
        }

        _logger.LogInformation("Webhook background service stopped");
    }

    private async Task ProcessWebhookAsync(WebhookProcessingItem item)
    {
        using var scope = _scopeFactory.CreateScope();

        try
        {
            _logger.LogInformation(
                "Processing webhook: EventType={EventType}, Action={Action}, PR={PrNumber}, Repository={Repository}",
                item.EventType,
                item.Action,
                item.Payload.PullRequest?.Number,
                item.Payload.Repository?.FullName);

            await SimulateProcessingWork();

            _logger.LogInformation("Successfully processed webhook item");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process webhook item");
            // TODO: Consider implementing retry logic or dead letter queue
        }
    }

    private static async Task SimulateProcessingWork()
    {
        // Simulate some work being done
        await Task.Delay(TimeSpan.FromSeconds(1));
    }
}
