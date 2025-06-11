using dissertation_backend.Services.Interfaces;
using Models.GithubModels.WebhookModels;
using System.Collections.Concurrent;

namespace dissertation_backend.Services.Implementations;

public class WebhookProcessingQueue : IWebhookProcessingQueue
{
    private readonly ConcurrentQueue<WebhookProcessingItem> _queue = new();
    private readonly SemaphoreSlim _semaphore = new(0);

    public void Enqueue(WebhookProcessingItem item)
    {
        _queue.Enqueue(item);
        _semaphore.Release();
    }

    public async Task<WebhookProcessingItem?> DequeueAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);

        _queue.TryDequeue(out var item);

        return item;
    }
}
