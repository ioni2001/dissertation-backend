namespace Models.GithubModels.ContextModels;

public class PullRequestContext
{
    public int PullRequestNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Repository { get; set; } = string.Empty;
    public List<ModifiedFileContext> ModifiedFiles { get; set; } = new();
    public List<ModifiedFileContext> RelatedFiles { get; set; } = new();
    public int EstimatedTokenCount { get; set; }
}
