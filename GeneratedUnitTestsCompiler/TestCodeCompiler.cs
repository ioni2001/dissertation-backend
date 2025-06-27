using LibGit2Sharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Models.CompilationModels;
using Models.GeminiModels;
using Models.GithubModels.Configuration;
using CliWrap;
using SignalRLogger;
using NUnit.Framework.Internal;

namespace GeneratedUnitTestsCompiler;

public class TestCodeCompiler : ITestCodeCompiler
{
    private readonly ILogger<TestCodeCompiler> _logger;
    private readonly string _tempDirectory;
    private readonly GitHub _gitHub;
    private readonly ISignalRLoggerService _signalRLoggerService;

    public TestCodeCompiler(ILogger<TestCodeCompiler> logger, GitHub gitHub, ISignalRLoggerService signalRLoggerService)
    {
        _logger = logger;
        _gitHub = gitHub;
        _tempDirectory = Path.Combine(Path.GetTempPath(), "RepoCompilation");
        _signalRLoggerService = signalRLoggerService;

        Directory.CreateDirectory(_tempDirectory);
    }

    public async Task<CompilationResult> CompileTestCodeAsync(GeneratedUnitTest unitTest, string repoPath, MSBuildWorkspace workspace)
    {
        var solutionPath = Directory.GetFiles(repoPath, "*.sln", SearchOption.AllDirectories).First();
        await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Information,
            $"Started compiling failed unit tests from file {unitTest.FileName} using Roslyn Compiler"));

        await Cli.Wrap("dotnet")
            .WithArguments($"restore \"{solutionPath}\"")
            .ExecuteAsync();

        var solution = await workspace.OpenSolutionAsync(solutionPath);
        var project = solution.Projects.FirstOrDefault(p => p.Name.Contains("UnitTests"));

        var projectParseOptions = project?.ParseOptions as CSharpParseOptions
            ?? throw new InvalidOperationException("Expected C# parse options");

        var testTree = CSharpSyntaxTree.ParseText(unitTest.TestCode, projectParseOptions);
        var compilation = await project!.GetCompilationAsync();

        var existingTestFile = project.Documents.FirstOrDefault(d =>
        Path.GetFileName(d.FilePath ?? "") == unitTest.FileName);

        if (existingTestFile != null)
        {
            var existingTree = await existingTestFile.GetSyntaxTreeAsync();
            if (existingTree != null)
            {
                compilation = compilation?.RemoveSyntaxTrees(existingTree);
                await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Information,
                        "Removed existing syntax tree for file: {test.FileName}"));
                _logger.LogInformation($"Removed existing syntax tree for file: {unitTest.FileName}");
            }
        }

        compilation = compilation?.AddSyntaxTrees(testTree);

        using var ms = new MemoryStream();
        var emitResult = compilation?.Emit(ms);
        if (emitResult is null)
        {
            _logger.LogError("Failed to compile generated unit test {Generated UnitTest}", unitTest.TestCode);
            await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Error,
                        $"Failed to compile generated unit test {unitTest.TestCode}"));

            return new CompilationResult { IsSuccessful = false };
        }

        var result = new CompilationResult
        {
            IsSuccessful = emitResult.Success,
            Errors = emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => new CompilationError
                {
                    ErrorCode = d.Id,
                    Message = d.GetMessage(),
                    LineNumber = d.Location.GetLineSpan().StartLinePosition.Line + 1,
                    ColumnNumber = d.Location.GetLineSpan().StartLinePosition.Character + 1,
                    Severity = d.Severity.ToString()
                })
                .ToList()
        };
        result.RepositoryContext = repoPath;
        unitTest.CompilationResult = result;

        return result;
    }

    public async Task<List<CompilationResult>> CompileAllTestsAsync(List<GeneratedUnitTest> tests, string repoPath, MSBuildWorkspace workspace)
    {
        await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Information,
            "Started compiling all generated unit tests using Roslyn Compiler"));

        var solutionPath = Directory.GetFiles(repoPath, "*.sln", SearchOption.AllDirectories).First();

        await Cli.Wrap("dotnet")
            .WithArguments($"restore \"{solutionPath}\"")
            .ExecuteAsync();

        var solution = await workspace.OpenSolutionAsync(solutionPath);
        var project = solution.Projects.FirstOrDefault(p => p.Name.Contains("UnitTests"));

        var projectParseOptions = project?.ParseOptions as CSharpParseOptions
            ?? throw new InvalidOperationException("Expected C# parse options");

        var results = new List<CompilationResult>();

        foreach (var test in tests)
        {
            var testTree = CSharpSyntaxTree.ParseText(test.TestCode, projectParseOptions);
            var compilation = await project!.GetCompilationAsync();

            var existingTestFile = project.Documents.FirstOrDefault(d =>
                Path.GetFileName(d.FilePath ?? "") == test.FileName);

            if (existingTestFile != null)
            {
                var existingTree = await existingTestFile.GetSyntaxTreeAsync();
                if (existingTree != null)
                {
                    compilation = compilation?.RemoveSyntaxTrees(existingTree);
                    await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Information,
                        "Removed existing syntax tree for file: {test.FileName}"));
                    _logger.LogInformation($"Removed existing syntax tree for file: {test.FileName}");
                }
            }

            compilation = compilation?.AddSyntaxTrees(testTree);

            using var ms = new MemoryStream();
            var emitResult = compilation?.Emit(ms);
            if (emitResult is null)
            {
                _logger.LogError("Failed to compile generated unit test {Generated UnitTest}", test.TestCode);
                await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Error,
                        $"Failed to compile generated unit test {test.TestCode}"));
                continue;
            }

            var result = new CompilationResult
            {
                IsSuccessful = emitResult.Success,
                Errors = emitResult.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => new CompilationError
                    {
                        ErrorCode = d.Id,
                        Message = d.GetMessage(),
                        LineNumber = d.Location.GetLineSpan().StartLinePosition.Line + 1,
                        ColumnNumber = d.Location.GetLineSpan().StartLinePosition.Character + 1,
                        Severity = d.Severity.ToString()
                    })
                    .ToList()
            };
            result.RepositoryContext = repoPath;
            test.CompilationResult = result;
            results.Add(result);
        }

        return results;
    }

    public async Task<string> CloneRepositoryAsync(string repositoryUrl, string branchName, string user)
    {
        var repoId = Guid.NewGuid().ToString("N")[..8];
        var repoPath = Path.Combine(_tempDirectory, $"repo_{repoId}");

        _logger.LogInformation("Cloning repository {Url} branch {Branch} to {Path}", repositoryUrl, branchName, repoPath);
        await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Information,
            $"Cloning repository {repositoryUrl} branch {branchName} to {repoPath}"));

        var cloneOptions = new CloneOptions
        {
            BranchName = branchName,
            Checkout = true
        };

        cloneOptions.FetchOptions.CredentialsProvider = (url, usernameFromUrl, types) =>
        {
            return new UsernamePasswordCredentials
            {
                Username = user,
                Password = _gitHub.Token
            };
        };

        Repository.Clone(repositoryUrl, repoPath, cloneOptions);

        _logger.LogInformation("Repository cloned successfully to {Path}", repoPath);
        await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Information,
            $"Repository cloned successfully to {repoPath}"));

        return repoPath;
    }

    public async Task CleanupRepositoryAsync(string repoPath)
    {
        if (!string.IsNullOrEmpty(repoPath) && Directory.Exists(repoPath))
        {
            try
            {
                Directory.Delete(repoPath, true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup repository directory: {Path}", repoPath);
                await _signalRLoggerService.SendLogAsync(BuildLog(Models.LoggingModels.LogLevel.Error,
                        $"Failed to cleanup repository directory: {repoPath}", ex.Message));
            }
        }
    }

    private static Models.LoggingModels.LogEntry BuildLog(Models.LoggingModels.LogLevel logLevel, string message, string? exception = "")
    {
        return new Models.LoggingModels.LogEntry() { Level = logLevel, Message = message, Exception = exception, Component = "TestCodeCompiler" };
    }
}
