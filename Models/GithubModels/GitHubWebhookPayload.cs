using System.Text.Json.Serialization;

namespace Models.GithubModels;

public class GitHubWebhookPayload
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("pull_request")]
    public PullRequestData? PullRequest { get; set; }

    [JsonPropertyName("repository")]
    public RepositoryData? Repository { get; set; }
}
