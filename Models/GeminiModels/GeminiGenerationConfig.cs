namespace Models.GeminiModels;

public class GeminiGenerationConfig
{
    public float Temperature { get; set; }
    public int TopK { get; set; }
    public float TopP { get; set; }
    public int MaxOutputTokens { get; set; }
    public string ResponseMimeType { get; set; } = string.Empty;
}
