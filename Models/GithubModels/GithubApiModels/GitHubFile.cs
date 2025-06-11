using System.Text.Json.Serialization;

namespace Models.GithubModels.GithubApiModels;

public class GitHubFile
{
    [JsonPropertyName("sha")]
    public string Sha { get; set; } = string.Empty;

    [JsonPropertyName("filename")]
    public string Filename { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("additions")]
    public int Additions { get; set; }

    [JsonPropertyName("deletions")]
    public int Deletions { get; set; }

    [JsonPropertyName("changes")]
    public int Changes { get; set; }

    [JsonPropertyName("patch")]
    public string Patch { get; set; } = string.Empty;

    [JsonPropertyName("contents_url")]
    public string ContentsUrl { get; set; } = string.Empty;
}
