using System.Text.Json.Serialization;

namespace Models.GithubModels.WebhookModels;

public class PullRequestData
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;

    [JsonPropertyName("head")]
    public BranchData? Head { get; set; }

    [JsonPropertyName("base")]
    public BranchData? Base { get; set; }

    [JsonPropertyName("diff_url")]
    public string DiffUrl { get; set; } = string.Empty;

    [JsonPropertyName("patch_url")]
    public string PatchUrl { get; set; } = string.Empty;
}