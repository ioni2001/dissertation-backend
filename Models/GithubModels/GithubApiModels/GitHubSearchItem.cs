using System.Text.Json.Serialization;

namespace Models.GithubModels.GithubApiModels;

public class GitHubSearchItem
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("sha")]
    public string Sha { get; set; } = string.Empty;

    [JsonPropertyName("score")]
    public double Score { get; set; }
}
