﻿using Models.GithubModels.WebhookModels;

namespace dissertation_backend.Services.Interfaces;

public interface IWebhookProcessingQueue
{
    void Enqueue(WebhookProcessingItem item);
    Task<WebhookProcessingItem?> DequeueAsync(CancellationToken cancellationToken);
}
