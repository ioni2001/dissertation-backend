namespace Models.GithubModels.ContextModels;

public class ModifiedFileContext
{
    public string FileName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Changes { get; set; }
    public string Patch { get; set; } = string.Empty;
    public string RelevantContent { get; set; } = string.Empty;
    public List<string> ExtractedClasses { get; set; } = new();
    public List<string> ExtractedMethods { get; set; } = new();
    public List<string> Dependencies { get; set; } = new();
}
