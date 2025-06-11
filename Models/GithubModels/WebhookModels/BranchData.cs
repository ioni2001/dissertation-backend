using System.Text.Json.Serialization;

namespace Models.GithubModels.WebhookModels;

public class BranchData
{
    [JsonPropertyName("ref")]
    public string Ref { get; set; } = string.Empty;

    [JsonPropertyName("sha")]
    public string Sha { get; set; } = string.Empty;
}
