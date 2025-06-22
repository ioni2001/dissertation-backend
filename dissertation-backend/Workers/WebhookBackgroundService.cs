using dissertation_backend.Services.Interfaces;
using GeminiIntegration;
using GeneratedUnitTestsCompiler;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis.MSBuild;
using Models.GithubModels.WebhookModels;

namespace dissertation_backend.Workers;

public class WebhookBackgroundService : BackgroundService
{
    private readonly IWebhookProcessingQueue _queue;
    private readonly ILogger<WebhookBackgroundService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MSBuildWorkspace _workspace;

    public WebhookBackgroundService(
        IWebhookProcessingQueue queue,
        ILogger<WebhookBackgroundService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _queue = queue;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _workspace = CreateWorkspace();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Webhook background service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var item = await _queue.DequeueAsync(stoppingToken);
                if (item != null)
                {
                    await ProcessWebhookAsync(item);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing webhook item");
            }
        }

        _logger.LogInformation("Webhook background service stopped");
    }

    private async Task ProcessWebhookAsync(WebhookProcessingItem item)
    {
        using var scope = _scopeFactory.CreateScope();
        try
        {
            _logger.LogInformation(
                "Processing webhook: EventType={EventType}, Action={Action}, PR={PrNumber}, Repository={Repository}",
                item.EventType,
                item.Action,
                item.Payload.PullRequest?.Number,
                item.Payload.Repository?.FullName);

            // Git data
            var gitUser = item.Payload.Repository.FullName.Split('/').First();
            var gitUrl = $"https://github.com/{item.Payload.Repository.FullName}.git";
            var sourceBranch = item.Payload.PullRequest.Head.Ref;

            // Services injection
            var gitHubRepositoryService = scope.ServiceProvider.GetRequiredService<IGitHubRepositoryService>();
            var geminiUnitTestGenerator = scope.ServiceProvider.GetRequiredService<IGeminiUnitTestGenerator>();
            var testCodeCompiler = scope.ServiceProvider.GetRequiredService<ITestCodeCompiler>();

            var context = await gitHubRepositoryService.GetPullRequestContextAsync(item.Payload);

            var response = await geminiUnitTestGenerator.GenerateUnitTestsAsync(context);

            var repoPath = testCodeCompiler.CloneRepository(gitUrl, sourceBranch, gitUser);

            await testCodeCompiler.CompileAllTestsAsync(response.GeneratedTests, repoPath, _workspace);

            for (int i = 0; i < response.GeneratedTests.Count; i++)
            {
                var test = response.GeneratedTests[i];

                while (!test.CompilationResult?.IsSuccessful ?? true)
                {
                    var regenerationResponse = await geminiUnitTestGenerator.RegenerateFailingUnitTestsAsync(context, test);
                    if (!regenerationResponse.Success)
                    {
                        continue;
                    }

                    test = regenerationResponse.GeneratedTests[0];

                    await testCodeCompiler.CompileTestCodeAsync(test, repoPath, _workspace);
                }

                response.GeneratedTests[i] = test;
            }

            testCodeCompiler.CleanupRepository(repoPath);

            _logger.LogInformation("Successfully processed webhook item");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process webhook item");
        }
    }

    private static MSBuildWorkspace CreateWorkspace()
    {
        MSBuildLocator.RegisterDefaults();

        var props = new Dictionary<string, string>{
              {"DisableBuild", "true"},
              {"SkipUnchangedProjectCheck", "true"}
            };

        return MSBuildWorkspace.Create(props);
    }
}
