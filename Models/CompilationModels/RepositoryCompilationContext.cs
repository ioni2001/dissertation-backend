namespace Models.CompilationModels;

public class RepositoryCompilationContext
{
    public string RepositoryPath { get; set; } = string.Empty;
    public string ProjectFile { get; set; } = string.Empty;
    public List<string> ProjectReferences { get; set; } = new();
    public List<string> PackageReferences { get; set; } = new();
    public string TargetFramework { get; set; } = string.Empty;
}
