namespace Models.GithubModels.PatchModels;

public class PatchChange
{
    public ChangeType Type { get; set; }
    public string Content { get; set; } = string.Empty;
}
