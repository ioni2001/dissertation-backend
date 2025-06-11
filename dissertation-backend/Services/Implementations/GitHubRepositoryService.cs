using dissertation_backend.Services.Interfaces;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using Models.GithubModels.WebhookModels;
using Models.GithubModels.ContextModels;
using Models.GithubModels.GithubApiModels;

namespace dissertation_backend.Services.Implementations;

public class GitHubRepositoryService : IGitHubRepositoryService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GitHubRepositoryService> _logger;
    private readonly ICodeAnalysisService _codeAnalysisService;

    public GitHubRepositoryService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<GitHubRepositoryService> logger,
        ICodeAnalysisService codeAnalysisService)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        _codeAnalysisService = codeAnalysisService;

        ConfigureHttpClient();
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
            var prioritizedFiles = await ApplyFileSelectionHeuristics(csharpFiles, payload.Repository.FullName);

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

    private async Task<List<GitHubFile>> GetPullRequestFilesAsync(string repository, int prNumber)
    {
        var url = $"https://api.github.com/repos/{repository}/pulls/{prNumber}/files";
        var response = await _httpClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to get PR files. Status: {StatusCode}", response.StatusCode);
            return new List<GitHubFile>();
        }

        var content = await response.Content.ReadAsStringAsync();
        var files = JsonSerializer.Deserialize<List<GitHubFile>>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return files ?? new List<GitHubFile>();
    }

    private async Task<List<GitHubFile>> ApplyFileSelectionHeuristics(List<GitHubFile> files, string repository)
    {
        var prioritizedFiles = new List<(GitHubFile file, int priority)>();

        foreach (var file in files)
        {
            var priority = CalculateFilePriority(file);
            prioritizedFiles.Add((file, priority));
        }

        // Sort by priority (higher is better) and take top files
        var maxFiles = _configuration.GetValue<int>("GitHub:MaxFilesPerPR", 20);
        return prioritizedFiles
            .OrderByDescending(x => x.priority)
            .Take(maxFiles)
            .Select(x => x.file)
            .ToList();
    }

    private int CalculateFilePriority(GitHubFile file)
    {
        var priority = 0;

        // Higher priority for files with more changes
        priority += Math.Min(file.Changes, 50); // Cap at 50 to avoid skewing

        // Prioritize business logic files over test files
        if (file.Filename.Contains("Test", StringComparison.OrdinalIgnoreCase) ||
            file.Filename.Contains("Spec", StringComparison.OrdinalIgnoreCase))
        {
            priority -= 20;
        }

        // Prioritize service/business logic files
        if (file.Filename.Contains("Service", StringComparison.OrdinalIgnoreCase) ||
            file.Filename.Contains("Controller", StringComparison.OrdinalIgnoreCase) ||
            file.Filename.Contains("Repository", StringComparison.OrdinalIgnoreCase) ||
            file.Filename.Contains("Manager", StringComparison.OrdinalIgnoreCase))
        {
            priority += 30;
        }

        // Lower priority for configuration files
        if (file.Filename.EndsWith(".json") || file.Filename.EndsWith(".xml") ||
            file.Filename.EndsWith(".config"))
        {
            priority -= 10;
        }

        // Prioritize files in certain directories
        var path = file.Filename.ToLower();
        if (path.Contains("/services/") || path.Contains("/controllers/") ||
            path.Contains("/business/") || path.Contains("/core/"))
        {
            priority += 20;
        }

        return priority;
    }

    private async Task<ModifiedFileContext?> GetFileContextAsync(GitHubFile file, string repository)
    {
        try
        {
            // Get full file content
            var fileContent = await GetFileContentAsync(repository, file.Filename);
            if (fileContent == null) return null;

            var context = new ModifiedFileContext
            {
                FileName = file.Filename,
                Status = file.Status,
                Changes = file.Changes,
                Patch = file.Patch
            };

            // Decode and analyze the file content
            var content = DecodeBase64Content(fileContent.Content);
            var analyzedContent = _codeAnalysisService.AnalyzeAndTruncateCode(content, file.Patch);

            context.RelevantContent = analyzedContent.TruncatedContent;
            context.ExtractedClasses = analyzedContent.Classes;
            context.ExtractedMethods = analyzedContent.Methods;
            context.Dependencies = analyzedContent.Dependencies;

            return context;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get context for file {FileName}", file.Filename);
            return null;
        }
    }

    private async Task<GitHubFileContent?> GetFileContentAsync(string repository, string filePath)
    {
        var url = $"https://api.github.com/repos/{repository}/contents/{filePath}";
        var response = await _httpClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to get file content for {FilePath}. Status: {StatusCode}",
                filePath, response.StatusCode);
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

        var maxRelatedFiles = _configuration.GetValue<int>("GitHub:MaxRelatedFiles", 7);
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
        var maxTotalTokens = _configuration.GetValue<int>("OpenAI:MaxTotalTokens", 2000000);
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
}
