using Models.GithubModels.ContextModels;
using Models.GithubModels.WebhookModels;

namespace dissertation_backend.Services.Interfaces;

public interface IGitHubRepositoryService
{
    Task<PullRequestContext> GetPullRequestContextAsync(GitHubWebhookPayload payload);
}
