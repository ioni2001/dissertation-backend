using System.Text.Json.Serialization;

namespace Models.GithubModels.GithubApiModels;

public class GitHubSearchResponse
{
    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }

    [JsonPropertyName("items")]
    public List<GitHubSearchItem> Items { get; set; } = new();
}
