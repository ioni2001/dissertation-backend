using dissertation_backend.Services.Interfaces;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using Models.GithubModels.WebhookModels;
using Models.GithubModels.ContextModels;
using Models.GithubModels.GithubApiModels;
using Models.GeminiModels;
using Octokit;
using Models.LoggingModels;
using SignalRLogger;

namespace dissertation_backend.Services.Implementations;

public class GitHubRepositoryService : IGitHubRepositoryService
{
    private readonly HttpClient _httpClient;
    private readonly GitHubClient _gitHubClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GitHubRepositoryService> _logger;
    private readonly ICodeAnalysisService _codeAnalysisService;
    private readonly IPatchMergerService _patchMergerService;
    private readonly ISignalRLoggerService _signalRLoggerService;

    public GitHubRepositoryService(
        HttpClient httpClient,
        GitHubClient gitHubClient,
        IConfiguration configuration,
        ILogger<GitHubRepositoryService> logger,
        ICodeAnalysisService codeAnalysisService,
        IPatchMergerService patchMergerService,
        ISignalRLoggerService signalRLoggerService)
    {
        _httpClient = httpClient;
        _gitHubClient = gitHubClient;
        _configuration = configuration;
        _logger = logger;
        _codeAnalysisService = codeAnalysisService;

        ConfigureHttpClient();
        _patchMergerService = patchMergerService;
        _signalRLoggerService = signalRLoggerService;
    }

    private void ConfigureHttpClient()
    {
        var token = _configuration["GitHub:Token"];
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException("GitHub token is required");

        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("DissertationBackend", "1.0"));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
    }

    public async Task<PullRequestContext> GetPullRequestContextAsync(GitHubWebhookPayload payload)
    {
        try
        {
            await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Information,
                 $"Started getting Pull Request Context: PR={payload.PullRequest.Number}, Title={payload.PullRequest.Title}, Description={payload.PullRequest.Body}, Repository={payload.Repository.FullName}"));
            var context = new PullRequestContext
            {
                PullRequestNumber = payload.PullRequest.Number,
                Title = payload.PullRequest.Title,
                Description = payload.PullRequest.Body,
                Repository = payload.Repository.FullName
            };

            // Get PR files
            var files = await GetPullRequestFilesAsync(payload.Repository.FullName, payload.PullRequest.Number);

            // Filter and prioritize C# files
            var csharpFiles = files.Where(f => f.Filename.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                                  .ToList();

            // Apply heuristics to get the most relevant context
            var prioritizedFiles = await ApplyFileSelectionHeuristics(csharpFiles);

            // Get detailed context for prioritized files
            foreach (var file in prioritizedFiles)
            {
                var fileContext = await GetFileContextAsync(file, payload.Repository.FullName);
                if (fileContext != null)
                {
                    context.ModifiedFiles.Add(fileContext);
                }
            }

            // Get related dependencies
            await EnrichWithDependencies(context, payload.Repository.FullName);

            return context;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving PR context for PR #{PrNumber}", payload.PullRequest.Number);
            throw;
        }
    }

    public async Task PushUnitTestsToPullRequestAsync(WebhookProcessingItem webhook, UnitTestGenerationResult unitTestsResult, string owner)
    {
        await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Information,
            $"Trying to push generated unit tests to the PR {webhook.Payload.PullRequest.Title}"));

        var headSha = webhook.Payload.PullRequest?.Head?.Sha;

        var baseTree = await _gitHubClient.Git.Tree.Get(owner, webhook.Payload?.Repository?.Name, headSha);
        var treeItems = new List<NewTreeItem>();

        foreach (var unitTest in unitTestsResult.GeneratedTests)
        {
            var filePath = $"UnitTests/{unitTest.FileName}";
         
            var newTreeItem = new NewTreeItem
            {
                Path = filePath,
                Mode = "100644",
                Type = TreeType.Blob,
                Content = unitTest.TestCode
            };

            treeItems.Add(newTreeItem);
        }

        if (treeItems.Count == 0)
        {
            _logger.LogInformation("No unit tests to push");
            await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Warning,
                "No unit tests to push"));

            return;
        }

        var newTree = new NewTree() { BaseTree = baseTree.Sha };
        foreach (var treeItem in treeItems)
        {
            newTree.Tree.Add(treeItem);
        }

        var newTreeResponse = await _gitHubClient.Git.Tree.Create(owner, webhook.Payload?.Repository?.Name, newTree);

        var commit = await _gitHubClient.Git.Commit.Create(owner, webhook.Payload?.Repository?.Name, new NewCommit(
            "Add/Update generated unit tests",
            newTreeResponse.Sha,
            headSha));

        await _gitHubClient.Git.Reference.Update(owner, webhook.Payload?.Repository?.Name, $"heads/{webhook.Payload?.PullRequest?.Head?.Ref}",
            new ReferenceUpdate(commit.Sha));

        _logger.LogInformation($"Successfully pushed {treeItems.Count} unit test files to PR #{webhook.Payload?.PullRequest?.Number}");
        await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Information,
                $"Successfully pushed {treeItems.Count} unit test files to PR {webhook.Payload?.PullRequest?.Title}"));
    }

    public async Task<bool> CheckPullRequestCommitValidityAsync(string owner, string repo, int pullRequestNumber)
    {
        var lastCommitName = await GetLastCommitMessageFromPullRequestAsync(owner, repo, pullRequestNumber);

        return !lastCommitName.Equals("Add/Update generated unit tests");
    }

    private async Task<List<GitHubFile>> GetPullRequestFilesAsync(string repository, int prNumber)
    {
        var url = $"https://api.github.com/repos/{repository}/pulls/{prNumber}/files";

        await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Information,
            $"Sending GET HTTP call to GitHub to receive PR context. Url {url}"));
        var response = await _httpClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to get PR files. Status: {StatusCode}", response.StatusCode);
            await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Error, $"Failed to get PR files. Status: {response.StatusCode}"));

            return new List<GitHubFile>();
        }

        var content = await response.Content.ReadAsStringAsync();
        var files = JsonSerializer.Deserialize<List<GitHubFile>>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return files ?? new List<GitHubFile>();
    }

    private async Task<List<GitHubFile>> ApplyFileSelectionHeuristics(List<GitHubFile> files)
    {
        await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Information,
            "Started applying file selection heuristics"));
        var prioritizedFiles = new List<(GitHubFile file, int priority)>();

        foreach (var file in files)
        {
            var priority = await CalculateFilePriority(file);
            prioritizedFiles.Add((file, priority));

            _logger.LogDebug("File {FileName} - Status: {Status}, Priority: {Priority}, Changes: {Changes}",
                file.Filename, file.Status, priority, file.Changes);

            await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Debug,
            $"File {file.Filename} - Status: {file.Status}, Priority: {priority}, Changes: {file.Changes}"));
        }

        // Sort by priority (higher is better) and take top files
        var maxFiles = _configuration.GetValue<int>("GitHub:MaxFilesPerPR", 10);
        var selectedFiles = prioritizedFiles
            .OrderByDescending(x => x.priority)
            .Take(maxFiles)
            .Select(x => x.file)
            .ToList();

        _logger.LogInformation("Selected {SelectedCount} files out of {TotalCount} C# files for processing",
            selectedFiles.Count, files.Count);
        await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Information,
            $"Selected {selectedFiles.Count} files out of {files.Count} C# files for processing"));

        return selectedFiles;
    }

    private async Task<int> CalculateFilePriority(GitHubFile file)
    {
        var priority = 0;

        // Base priority for changes (cap to prevent skewing)
        priority += Math.Min(file.Changes, 100);

        // HIGHEST priority for new files (we definitely want to test new code)
        if (file.Status == "added")
        {
            priority += 200;
            _logger.LogDebug("New file {FileName} gets +200 priority", file.Filename);
            await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Debug,
            $"New file {file.Filename} gets +200 priority"));
        }

        // HIGH priority for modified files
        if (file.Status == "modified")
        {
            priority += 150;
            _logger.LogDebug("Modified file {FileName} gets +150 priority", file.Filename);
            await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Debug,
            $"Modified file {file.Filename} gets +150 priority"));
        }

        // LOWER priority for deleted files (might still be useful for context)
        if (file.Status == "removed")
        {
            priority += 10;
            _logger.LogDebug("Deleted file {FileName} gets +10 priority", file.Filename);
            await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Debug,
            $"Deleted file {file.Filename} gets +10 priority"));
        }

        // Prioritize business logic files over test files
        if (file.Filename.Contains("Test", StringComparison.OrdinalIgnoreCase) ||
            file.Filename.Contains("Spec", StringComparison.OrdinalIgnoreCase) ||
            file.Filename.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase))
        {
            priority -= 50; // Reduced penalty since we might want to see existing tests
            _logger.LogDebug("Test file {FileName} gets -50 priority", file.Filename);
            await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Debug,
            $"Test file {file.Filename} gets -50 priority"));
        }

        // HIGH priority for important business logic patterns
        var fileName = file.Filename.ToLower();
        if (fileName.Contains("service") || fileName.Contains("controller") ||
            fileName.Contains("repository") || fileName.Contains("manager") ||
            fileName.Contains("handler") || fileName.Contains("processor"))
        {
            priority += 80;
            _logger.LogDebug("Business logic file {FileName} gets +80 priority", file.Filename);
            await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Debug,
            $"Business logic file {file.Filename} gets +80 priority"));
        }

        // MEDIUM priority for model/entity files
        if (fileName.Contains("model") || fileName.Contains("entity") ||
            fileName.Contains("dto") || fileName.Contains("request") ||
            fileName.Contains("response"))
        {
            priority += 60;
            _logger.LogDebug("Model file {FileName} gets +60 priority", file.Filename);
            await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Debug,
            $"Model file {file.Filename} gets +60 priority"));
        }

        // Prioritize files in important directories
        var path = file.Filename.ToLower();
        if (path.Contains("/services/") || path.Contains("/controllers/") ||
            path.Contains("/business/") || path.Contains("/core/") ||
            path.Contains("/domain/") || path.Contains("/application/"))
        {
            priority += 50;
            _logger.LogDebug("Important directory file {FileName} gets +50 priority", file.Filename);
            await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Debug,
            $"Important directory file {file.Filename} gets +50 priority"));
        }

        // Lower priority for infrastructure/framework files
        if (path.Contains("/infrastructure/") || path.Contains("/framework/") ||
            path.Contains("/migrations/") || path.Contains("program.cs") ||
            path.Contains("startup.cs"))
        {
            priority -= 20;
            _logger.LogDebug("Infrastructure file {FileName} gets -20 priority", file.Filename);
            await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Debug,
            $"Infrastructure file {file.Filename} gets -20 priority"));
        }

        _logger.LogDebug("Final priority for {FileName}: {Priority}", file.Filename, priority);
        await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Debug,
            $"Final priority for {file.Filename}: {priority}"));
        return Math.Max(priority, 0); // Ensure non-negative priority
    }

    private async Task<ModifiedFileContext?> GetFileContextAsync(GitHubFile file, string repository)
    {
        try
        {
            var context = new ModifiedFileContext
            {
                FileName = file.Filename,
                Status = file.Status,
                Changes = file.Changes,
                Patch = file.Patch
            };

            string finalContent;

            if (file.Status == "added")
            {
                // For new files, reconstruct content from patch
                finalContent = ReconstructNewFileFromPatch(file.Patch);
                _logger.LogInformation("Reconstructed new file {FileName} with {Lines} lines",
                    file.Filename, finalContent.Split('\n').Length);
                await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Information,
                    $"Reconstructed new file {file.Filename} with {finalContent.Split('\n').Length} lines"));
            }
            else if (file.Status == "removed")
            {
                // Deleted files are ignored
                _logger.LogInformation("Skipping deleted file {FileName}", file.Filename);
                await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Information,
                    $"Skipping deleted file {file.Filename}"));

                return null;
            }
            else
            {
                // For modified files, get the current content (after changes)
                var fileContent = await GetFileContentAsync(repository, file.Filename);
                if (fileContent == null)
                {
                    _logger.LogWarning("Could not retrieve content for modified file {FileName}", file.Filename);
                    await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Warning,
                    $"Could not retrieve content for modified file {file.Filename}"));

                    return null;
                }
                var decodedContent = DecodeBase64Content(fileContent.Content);
                if (decodedContent == null)
                {
                    _logger.LogWarning("Could not decode retrieved content for modified file {FileName}", file.Filename);
                    await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Warning,
                    $"Could not decode retrieved content for modified file {file.Filename}"));

                    return null;
                }

                finalContent = _patchMergerService.MergePatchWithContent(decodedContent, file.Patch);

                _logger.LogInformation("Retrieved current content for modified file {FileName}", file.Filename);
                await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Information,
                    $"Retrieved current content for modified file {file.Filename}"));
            }

            // Analyze the final content (new shape of the file)
            var analyzedContent = _codeAnalysisService.AnalyzeAndTruncateCode(finalContent, file.Patch);

            context.RelevantContent = analyzedContent.TruncatedContent;
            context.ExtractedClasses = analyzedContent.Classes;
            context.ExtractedMethods = analyzedContent.Methods;
            context.Dependencies = analyzedContent.Dependencies;

            return context;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get context for file {FileName}: {Error}", file.Filename, ex.Message);
            await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Error,
                    $"Failed to get context for file {file.Filename}", ex.Message));

            return null;
        }
    }

    private string ReconstructNewFileFromPatch(string patch)
    {
        if (string.IsNullOrEmpty(patch))
        {
            return string.Empty;
        }

        var lines = patch.Split('\n');
        var fileContent = new List<string>();
        var inFileContent = false;

        foreach (var line in lines)
        {
            // Skip diff headers
            if (line.StartsWith("@@"))
            {
                inFileContent = true;
                continue;
            }

            if (!inFileContent) continue;

            // For new files (added), we want lines that start with + (but not +++)
            if (line.StartsWith("+") && !line.StartsWith("+++"))
            {
                fileContent.Add(line.Substring(1)); // Remove the + prefix
            }
            // For context lines (no prefix), include them as well
            else if (!line.StartsWith("-") && !line.StartsWith("+++") && !line.StartsWith("---"))
            {
                fileContent.Add(line);
            }
        }

        return string.Join('\n', fileContent);
    }

    private async Task<GitHubFileContent?> GetFileContentAsync(string repository, string filePath)
    {
        var url = $"https://api.github.com/repos/{repository}/contents/{filePath}";

        await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Information,
            $"Sending GET HTTP call to GitHub to receive file content. Url {url}"));

        var response = await _httpClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to get file content for {FilePath}. Status: {StatusCode}",
                filePath, response.StatusCode);
            await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Warning,
            $"Failed to get file content for {filePath}. Status: {response.StatusCode}"));

            return null;
        }

        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<GitHubFileContent>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    private string DecodeBase64Content(string base64Content)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64Content);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decode base64 content");
            return string.Empty;
        }
    }

    private async Task EnrichWithDependencies(PullRequestContext context, string repository)
    {
        var dependencies = new HashSet<string>();

        foreach (var file in context.ModifiedFiles)
        {
            dependencies.UnionWith(file.Dependencies);
        }

        // Get related files based on dependencies
        var relatedFiles = await GetRelatedFilesAsync(repository, dependencies.ToList());

        var maxRelatedFiles = _configuration.GetValue<int>("GitHub:MaxRelatedFiles", 5);
        foreach (var relatedFile in relatedFiles.Take(maxRelatedFiles))
        {
            if (!context.ModifiedFiles.Any(f => f.FileName == relatedFile.FileName))
            {
                context.RelatedFiles.Add(relatedFile);
            }
        }

        // Calculate total estimated token count
        var totalTokens = context.ModifiedFiles.Sum(f => _codeAnalysisService.EstimateTokenCount(f.RelevantContent)) +
                         context.RelatedFiles.Sum(f => _codeAnalysisService.EstimateTokenCount(f.RelevantContent));

        context.EstimatedTokenCount = totalTokens;

        // If we're over the limit, apply additional truncation
        var maxTotalTokens = _configuration.GetValue<int>("GeminiAI:MaxTotalTokens", 8000);
        if (totalTokens > maxTotalTokens)
        {
            await ApplyGlobalTruncation(context, maxTotalTokens);
        }
    }

    private async Task ApplyGlobalTruncation(PullRequestContext context, int maxTokens)
    {
        // Prioritize modified files over related files
        var modifiedFilesTokens = (int)(maxTokens * 0.8); // 80% for modified files
        var relatedFilesTokens = maxTokens - modifiedFilesTokens; // 20% for related files

        // Truncate modified files proportionally
        if (context.ModifiedFiles.Any())
        {
            var totalModifiedTokens = context.ModifiedFiles.Sum(f =>
                _codeAnalysisService.EstimateTokenCount(f.RelevantContent));

            if (totalModifiedTokens > modifiedFilesTokens)
            {
                foreach (var file in context.ModifiedFiles)
                {
                    var currentTokens = _codeAnalysisService.EstimateTokenCount(file.RelevantContent);
                    var targetTokens = (int)((double)currentTokens / totalModifiedTokens * modifiedFilesTokens);

                    if (currentTokens > targetTokens)
                    {
                        file.RelevantContent = TruncateToTokenLimit(file.RelevantContent, targetTokens);
                    }
                }
            }
        }

        // Truncate or remove related files
        if (context.RelatedFiles.Any())
        {
            var currentRelatedTokens = context.RelatedFiles.Sum(f =>
                _codeAnalysisService.EstimateTokenCount(f.RelevantContent));

            if (currentRelatedTokens > relatedFilesTokens)
            {
                // Remove least important related files first
                var sortedRelatedFiles = context.RelatedFiles
                    .OrderByDescending(f => f.Changes) // Prioritize files with more changes
                    .ThenByDescending(f => f.ExtractedMethods.Count) // Then by method count
                    .ToList();

                context.RelatedFiles.Clear();
                var runningTokens = 0;

                foreach (var file in sortedRelatedFiles)
                {
                    var fileTokens = _codeAnalysisService.EstimateTokenCount(file.RelevantContent);
                    if (runningTokens + fileTokens <= relatedFilesTokens)
                    {
                        context.RelatedFiles.Add(file);
                        runningTokens += fileTokens;
                    }
                }
            }
        }

        // Recalculate final token count
        context.EstimatedTokenCount =
            context.ModifiedFiles.Sum(f => _codeAnalysisService.EstimateTokenCount(f.RelevantContent)) +
            context.RelatedFiles.Sum(f => _codeAnalysisService.EstimateTokenCount(f.RelevantContent));
    }

    private string TruncateToTokenLimit(string content, int maxTokens)
    {
        var currentTokens = _codeAnalysisService.EstimateTokenCount(content);
        if (currentTokens <= maxTokens) return content;

        var lines = content.Split('\n');
        var ratio = (double)maxTokens / currentTokens;
        var targetLineCount = (int)(lines.Length * ratio);

        // Keep the most important lines (assuming they're at the beginning after prioritization)
        var truncatedLines = lines.Take(targetLineCount).ToArray();

        return string.Join('\n', truncatedLines) + "\n// ... [Content truncated to fit token limits] ...";
    }

    private async Task<List<ModifiedFileContext>> GetRelatedFilesAsync(string repository, List<string> dependencies)
    {
        var relatedFiles = new List<ModifiedFileContext>();

        // Focus on interface and abstract class dependencies for test generation
        var priorityDependencies = dependencies
            .Where(d => d.StartsWith("I") && char.IsUpper(d[1])) // Interfaces
            .Concat(dependencies.Where(d => d.Contains("Abstract") || d.Contains("Base"))) // Abstract classes
            .Take(5) // Limit to avoid rate limiting
            .ToList();

        foreach (var dependency in priorityDependencies)
        {
            try
            {
                // Search for files containing this dependency
                var searchUrl = $"https://api.github.com/search/code?q={Uri.EscapeDataString(dependency)}+repo:{repository}+extension:cs&per_page=3";
                var response = await _httpClient.GetAsync(searchUrl);

                if (response.IsSuccessStatusCode)
                {
                    var searchResult = await response.Content.ReadAsStringAsync();
                    var searchResponse = JsonSerializer.Deserialize<GitHubSearchResponse>(searchResult);

                    if (searchResponse?.Items != null)
                    {
                        foreach (var item in searchResponse.Items.Take(2)) // Limit results per dependency
                        {
                            var fileContent = await GetFileContentAsync(repository, item.Path);
                            if (fileContent != null)
                            {
                                var content = DecodeBase64Content(fileContent.Content);
                                var analyzedContent = _codeAnalysisService.AnalyzeAndTruncateCode(content, string.Empty);

                                // Only include if it has relevant methods/classes
                                if (analyzedContent.Methods.Any() || analyzedContent.Classes.Any())
                                {
                                    relatedFiles.Add(new ModifiedFileContext
                                    {
                                        FileName = item.Path,
                                        Status = "related",
                                        RelevantContent = analyzedContent.TruncatedContent,
                                        ExtractedClasses = analyzedContent.Classes,
                                        ExtractedMethods = analyzedContent.Methods,
                                        Dependencies = analyzedContent.Dependencies
                                    });
                                }
                            }
                        }
                    }
                }

                // Add delay to avoid rate limiting
                await Task.Delay(100);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to search for dependency {Dependency}", dependency);
            }
        }

        return relatedFiles;
    }

    private async Task<string> GetLastCommitMessageFromPullRequestAsync(string owner, string repo, int pullRequestNumber)
    {
        var pullRequest = await _gitHubClient.PullRequest.Get(owner, repo, pullRequestNumber);
        var commit = await _gitHubClient.Repository.Commit.Get(owner, repo, pullRequest.Head.Sha);

        return commit.Commit.Message;
    }

    private static LogEntry BuildLog(Models.LoggingModels.LogLevel logLevel, string message, string? exception = "")
    {
        return new LogEntry() { Level = logLevel, Message = message, Exception = exception, Component = "GitHubRepositoryService" };
    }
}
