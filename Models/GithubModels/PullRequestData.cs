using System.Text.Json.Serialization;

namespace Models.GithubModels;

public class PullRequestData
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;

    [JsonPropertyName("head")]
    public BranchData? Head { get; set; }

    [JsonPropertyName("base")]
    public BranchData? Base { get; set; }
}