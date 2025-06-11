using Models.GithubModels.ContextModels;

namespace dissertation_backend.Services.Interfaces;

public interface ICodeAnalysisService
{
    AnalyzedCode AnalyzeAndTruncateCode(string content, string patch);
    int EstimateTokenCount(string text);
    List<string> ExtractDependencies(string content);
}
