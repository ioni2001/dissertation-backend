using Models.GeminiModels;
using Models.GithubModels.ContextModels;
using Models.GithubModels.WebhookModels;

namespace dissertation_backend.Services.Interfaces;

public interface IGitHubRepositoryService
{
    Task<PullRequestContext> GetPullRequestContextAsync(GitHubWebhookPayload payload);

    Task PushUnitTestsToPullRequestAsync(WebhookProcessingItem webhook, UnitTestGenerationResult unitTestsResult, string owner);

    Task<bool> CheckPullRequestCommitValidityAsync(string owner, string repo, int pullRequestNumber);
}
