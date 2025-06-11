namespace Models.GithubModels.WebhookModels;

public class WebhookProcessingItem
{
    public string EventType { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public GitHubWebhookPayload Payload { get; set; } = new();
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}
