namespace Models.CompilationModels;

public class CompilationResult
{
    public bool IsSuccessful { get; set; }

    public List<CompilationError> Errors { get; set; } = new();

    public TimeSpan CompilationTime { get; set; }

    public string RepositoryContext { get; set; } = string.Empty;
}
