namespace Models.GeminiModels;

public class UnitTestGenerationResult
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public List<GeneratedUnitTest> GeneratedTests { get; set; } = new();
    public GeminiUsageMetadata? UsageMetadata { get; set; }
}
