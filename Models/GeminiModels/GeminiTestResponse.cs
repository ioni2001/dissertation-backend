namespace Models.GeminiModels;

public class GeminiTestResponse
{
    public bool Success { get; set; }
    public List<GeneratedUnitTest>? GeneratedTests { get; set; }
    public List<string>? Recommendations { get; set; }
    public string? MockingStrategy { get; set; }
}
