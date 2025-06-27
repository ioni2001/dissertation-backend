using dissertation_backend.Services.Interfaces;
using GeminiIntegration;
using GeneratedUnitTestsCompiler;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis.MSBuild;
using Models.GithubModels.WebhookModels;
using Models.LoggingModels;
using SignalRLogger;

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
            var signalRLoggerService = scope.ServiceProvider.GetRequiredService<ISignalRLoggerService>();

            _logger.LogInformation(
                "Processing webhook: EventType={EventType}, Action={Action}, PR={PrNumber}, Repository={Repository}",
                item.EventType,
                item.Action,
                item.Payload.PullRequest?.Number,
                item.Payload.Repository?.FullName);
            await signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Information, 
                $"Processing webhook: EventType={item.EventType}, Action={item.Action}, PR={item.Payload.PullRequest?.Number}, Repository={item.Payload.Repository?.FullName}"));

            // Git data
            var gitUser = item.Payload.Repository.FullName.Split('/').First();
            var gitUrl = $"https://github.com/{item.Payload.Repository.FullName}.git";
            var sourceBranch = item.Payload.PullRequest.Head.Ref;

            // Services injection
            var gitHubRepositoryService = scope.ServiceProvider.GetRequiredService<IGitHubRepositoryService>();
            var geminiUnitTestGenerator = scope.ServiceProvider.GetRequiredService<IGeminiUnitTestGenerator>();
            var testCodeCompiler = scope.ServiceProvider.GetRequiredService<ITestCodeCompiler>();

            // Check whether PR commit should be processed or not
            if (!await gitHubRepositoryService.CheckPullRequestCommitValidityAsync(gitUser, item.Payload.Repository.Name, item.Payload.PullRequest.Number))
            {
                _logger.LogWarning("Last commit was automatically created with generated unit tests. Skipping...");
                await signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Warning,
                    "Last commit was automatically created with generated unit tests. Skipping processing..."));

                return;
            }

            var context = await gitHubRepositoryService.GetPullRequestContextAsync(item.Payload);

            var response = await geminiUnitTestGenerator.GenerateUnitTestsAsync(context);

            var repoPath = await testCodeCompiler.CloneRepositoryAsync(gitUrl, sourceBranch, gitUser);

            await testCodeCompiler.CompileAllTestsAsync(response.GeneratedTests, repoPath, _workspace);

            // Retry for unit tests compilation
            for (int i = 0; i < response.GeneratedTests.Count; i++)
            {
                var test = response.GeneratedTests[i];
                var maxAttempts = 5;

                while (!test.CompilationResult?.IsSuccessful ?? true && (maxAttempts > 0))
                {
                    maxAttempts--;

                    var regenerationResponse = await geminiUnitTestGenerator.RegenerateFailingUnitTestsAsync(context, test);
                    if (!regenerationResponse.Success)
                    {
                        continue;
                    }

                    test = regenerationResponse.GeneratedTests[0];

                    await testCodeCompiler.CompileTestCodeAsync(test, repoPath, _workspace);
                }

                if (maxAttempts == 0)
                {
                    await signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Warning,
                        $"Removing unit tests class {test.ClassName} becuase it failed compilation after 5 attempts"));

                    response.GeneratedTests.RemoveAt(i);
                    i--;
                }
                else
                {
                    await signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Information,
                        $"Successfully compiled unit tests class {test.ClassName} after {5 - maxAttempts} attempts"));

                    response.GeneratedTests[i] = test;
                }
            }

            await testCodeCompiler.CleanupRepositoryAsync(repoPath);

            await gitHubRepositoryService.PushUnitTestsToPullRequestAsync(item, response, gitUser);

            _logger.LogInformation("Successfully processed webhook item");
            await signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Information,
                        "Successfully processed GitHub webhook event"));
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

    private static LogEntry BuildLog(Models.LoggingModels.LogLevel logLevel, string message, string? exception = "")
    {
        return new LogEntry() { Level = logLevel, Message = message, Exception = exception, Component = "WebhookBackgroundService" };
    }
}
