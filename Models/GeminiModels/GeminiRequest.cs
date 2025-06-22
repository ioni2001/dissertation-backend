namespace Models.GeminiModels;

public class GeminiRequest
{
    public List<GeminiContent> Contents { get; set; } = new();
    public GeminiGenerationConfig? GenerationConfig { get; set; }
    public List<GeminiSafetySetting> SafetySettings { get; set; } = new();
}
