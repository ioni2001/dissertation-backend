namespace Models.GeminiModels;

public class GeminiResponse
{
    public List<GeminiCandidate>? Candidates { get; set; }
    public GeminiUsageMetadata? UsageMetadata { get; set; }
}
