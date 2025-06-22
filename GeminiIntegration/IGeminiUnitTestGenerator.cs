using Models.GeminiModels;
using Models.GithubModels.ContextModels;

namespace GeminiIntegration;

public interface IGeminiUnitTestGenerator
{
    Task<UnitTestGenerationResult> GenerateUnitTestsAsync(PullRequestContext prContext);

    Task<UnitTestGenerationResult> RegenerateFailingUnitTestsAsync(PullRequestContext prContext, GeneratedUnitTest failedUnitTest);
}
