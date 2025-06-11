namespace Models.GithubModels.ContextModels;

public class AnalyzedCode
{
    public string TruncatedContent { get; set; } = string.Empty;
    public List<string> Classes { get; set; } = new();
    public List<string> Methods { get; set; } = new();
    public List<string> Dependencies { get; set; } = new();
    public int EstimatedTokenCount { get; set; }
}
