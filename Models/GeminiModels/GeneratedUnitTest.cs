using Models.CompilationModels;

namespace Models.GeminiModels;

public class GeneratedUnitTest
{
    public string ClassName { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string TestCode { get; set; } = string.Empty;

    public CompilationResult? CompilationResult { get; set; }
}
