namespace Models.GithubModels.PatchModels;

public class PatchHunk
{
    public int OriginalStartLine { get; set; }
    public int OriginalLineCount { get; set; }
    public int NewStartLine { get; set; }
    public int NewLineCount { get; set; }
    public List<PatchChange> Changes { get; set; } = new List<PatchChange>();
}
