using Microsoft.CodeAnalysis.MSBuild;
using Models.CompilationModels;
using Models.GeminiModels;

namespace GeneratedUnitTestsCompiler;

public interface ITestCodeCompiler
{
    Task<CompilationResult> CompileTestCodeAsync(GeneratedUnitTest unitTest, string repoPath, MSBuildWorkspace workspace);

    Task<List<CompilationResult>> CompileAllTestsAsync(List<GeneratedUnitTest> tests, string repoPath, MSBuildWorkspace workspace);

    string CloneRepository(string repositoryUrl, string branchName, string user);

    void CleanupRepository(string repoPath);
}
