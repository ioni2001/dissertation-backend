using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text;
using Models.GeminiModels;
using Models.GithubModels.ContextModels;
using Microsoft.Extensions.Configuration;

namespace GeminiIntegration;

public class GeminiUnitTestGenerator : IGeminiUnitTestGenerator
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GeminiUnitTestGenerator> _logger;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _baseUrl;

    public GeminiUnitTestGenerator(HttpClient httpClient, IConfiguration configuration, ILogger<GeminiUnitTestGenerator> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiKey = configuration["GeminiAI:ApiKey"] ?? throw new InvalidOperationException("Gemini API key not configured");
        _model = configuration["GeminiAI:Model"] ?? "gemini-1.5-pro";
        _baseUrl = "https://generativelanguage.googleapis.com/v1beta";
    }

    public async Task<UnitTestGenerationResult> GenerateUnitTestsAsync(PullRequestContext prContext)
    {
        try
        {
            var prompt = BuildUnitTestPrompt(prContext);

            _logger.LogInformation("Generating unit tests for PR #{PrNumber} with {FileCount} modified files using Gemini",
                prContext.PullRequestNumber, prContext.ModifiedFiles.Count);

            var request = new GeminiRequest
            {
                Contents = new List<GeminiContent>
                {
                    new GeminiContent
                    {
                        Parts = new List<GeminiPart>
                        {
                            new GeminiPart { Text = GetSystemPrompt() + "\n\n" + prompt }
                        }
                    }
                },
                GenerationConfig = new GeminiGenerationConfig
                {
                    Temperature = 0.1f,
                    TopK = 40,
                    TopP = 0.95f,
                    MaxOutputTokens = 8192,
                    ResponseMimeType = "application/json"
                },
                SafetySettings = new List<GeminiSafetySetting>
                {
                    new GeminiSafetySetting
                    {
                        Category = "HARM_CATEGORY_HARASSMENT",
                        Threshold = "BLOCK_MEDIUM_AND_ABOVE"
                    },
                    new GeminiSafetySetting
                    {
                        Category = "HARM_CATEGORY_HATE_SPEECH",
                        Threshold = "BLOCK_MEDIUM_AND_ABOVE"
                    },
                    new GeminiSafetySetting
                    {
                        Category = "HARM_CATEGORY_SEXUALLY_EXPLICIT",
                        Threshold = "BLOCK_MEDIUM_AND_ABOVE"
                    },
                    new GeminiSafetySetting
                    {
                        Category = "HARM_CATEGORY_DANGEROUS_CONTENT",
                        Threshold = "BLOCK_MEDIUM_AND_ABOVE"
                    }
                }
            };

            var response = await CallGeminiApiAsync(request);

            if (response?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text == null)
            {
                throw new InvalidOperationException("Empty response from Gemini AI");
            }

            var responseText = response.Candidates.First().Content.Parts.First().Text;
            var result = ParseResponse(responseText);

            _logger.LogInformation("Successfully generated {TestCount} unit tests for PR #{PrNumber}",
                result.GeneratedTests.Count, prContext.PullRequestNumber);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating unit tests for PR #{PrNumber}", prContext.PullRequestNumber);
            return new UnitTestGenerationResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                GeneratedTests = new List<GeneratedUnitTest>()
            };
        }
    }

    public async Task<UnitTestGenerationResult> RegenerateFailingUnitTestsAsync(PullRequestContext prContext, GeneratedUnitTest failedUnitTest)
    {
        try
        {
            var prompt = BuildTestRegenerationPrompt(failedUnitTest, prContext);
            

            _logger.LogInformation("Regenerating unit tests for PR #{PrNumber} with {FileCount} modified files using Gemini",
                prContext.PullRequestNumber, prContext.ModifiedFiles.Count);

            var request = new GeminiRequest
            {
                Contents = new List<GeminiContent>
                {
                    new GeminiContent
                    {
                        Parts = new List<GeminiPart>
                        {
                            new GeminiPart { Text = GetRegenerationSystemPrompt() + "\n\n" + prompt }
                        }
                    }
                },
                GenerationConfig = new GeminiGenerationConfig
                {
                    Temperature = 0.1f,
                    TopK = 40,
                    TopP = 0.95f,
                    MaxOutputTokens = 8192,
                    ResponseMimeType = "application/json"
                },
                SafetySettings = new List<GeminiSafetySetting>
                {
                    new GeminiSafetySetting
                    {
                        Category = "HARM_CATEGORY_HARASSMENT",
                        Threshold = "BLOCK_MEDIUM_AND_ABOVE"
                    },
                    new GeminiSafetySetting
                    {
                        Category = "HARM_CATEGORY_HATE_SPEECH",
                        Threshold = "BLOCK_MEDIUM_AND_ABOVE"
                    },
                    new GeminiSafetySetting
                    {
                        Category = "HARM_CATEGORY_SEXUALLY_EXPLICIT",
                        Threshold = "BLOCK_MEDIUM_AND_ABOVE"
                    },
                    new GeminiSafetySetting
                    {
                        Category = "HARM_CATEGORY_DANGEROUS_CONTENT",
                        Threshold = "BLOCK_MEDIUM_AND_ABOVE"
                    }
                }
            };

            var response = await CallGeminiApiAsync(request);

            if (response?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text == null)
            {
                throw new InvalidOperationException("Empty response from Gemini AI");
            }

            var responseText = response.Candidates.First().Content.Parts.First().Text;
            var result = ParseResponse(responseText);

            _logger.LogInformation("Successfully regenerated {TestCount} unit tests for PR #{PrNumber}",
                result.GeneratedTests.Count, prContext.PullRequestNumber);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating unit tests for PR #{PrNumber}", prContext.PullRequestNumber);
            return new UnitTestGenerationResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                GeneratedTests = new List<GeneratedUnitTest>()
            };
        }
    }

    private async Task<GeminiResponse?> CallGeminiApiAsync(GeminiRequest request)
    {
        var url = $"{_baseUrl}/models/{_model}:generateContent?key={_apiKey}";

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        var jsonContent = JsonSerializer.Serialize(request, jsonOptions);
        var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        _logger.LogDebug("Calling Gemini API with {ContentLength} characters", jsonContent.Length);

        var response = await _httpClient.PostAsync(url, httpContent);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("Gemini AI API error: {StatusCode} - {Error}", response.StatusCode, errorContent);
            throw new HttpRequestException($"Gemini AI API error: {response.StatusCode} - {errorContent}");
        }

        var responseContent = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<GeminiResponse>(responseContent, jsonOptions);
    }

    private string GetSystemPrompt()
    {
        return @"You are an expert C# developer and unit testing specialist. Your task is to analyze pull request changes and generate comprehensive unit tests that cover the modified code.

REQUIREMENTS:
1. Generate unit tests using NUnit framework with FluentAssertions
2. Focus ONLY on testing the modified/added code, not existing functionality
3. Create tests for edge cases, error scenarios, and happy paths
4. Use proper mocking with NSubstitute for dependencies
5. Follow AAA pattern (Arrange, Act, Assert)
6. Include descriptive test names that explain what is being tested
7. Consider async/await patterns where applicable

RESPONSE FORMAT:
Return a JSON object with this structure:
{
  ""success"": true,
  ""generatedTests"": [
    {
      ""className"": ""ClassNameTests"",
      ""fileName"": ""ClassNameTests.cs"",
      ""testCode"": ""complete C# test class code""
    }
  ]
}

TESTING BEST PRACTICES:
- Test one thing per test method
- Use meaningful test data
- Mock external dependencies
- Test both success and failure scenarios
- Verify exceptions are thrown when expected
- Test boundary conditions
- Use parameterized tests for multiple similar scenarios";
    }

    private string BuildUnitTestPrompt(PullRequestContext prContext)
    {
        var promptBuilder = new StringBuilder();

        promptBuilder.AppendLine($"# Pull Request Analysis for Unit Test Generation");
        promptBuilder.AppendLine($"**PR #{prContext.PullRequestNumber}**: {prContext.Title}");
        promptBuilder.AppendLine($"**Repository**: {prContext.Repository}");
        promptBuilder.AppendLine();

        if (!string.IsNullOrEmpty(prContext.Description))
        {
            promptBuilder.AppendLine($"**Description**: {prContext.Description}");
            promptBuilder.AppendLine();
        }

        AppendModifiedFilesContext(promptBuilder, prContext);

        promptBuilder.AppendLine("## Task");
        promptBuilder.AppendLine("Generate comprehensive unit tests for the modified code above. Focus on:");
        promptBuilder.AppendLine("1. Testing new methods and classes");
        promptBuilder.AppendLine("2. Testing modified logic and edge cases");
        promptBuilder.AppendLine("3. Ensuring proper error handling coverage");
        promptBuilder.AppendLine("4. Mocking dependencies appropriately");
        promptBuilder.AppendLine("5. Following C# and NUnit best practices");
        promptBuilder.AppendLine("6. **Do not overcomplicate them, please give the response in a valid parsable json in the format I requested you by not adding characters that might affect parsing like '@'**");
        promptBuilder.AppendLine("7. **For async methods use this syntax _dependency.Method().Returns(Task.FromException<ReturnType>(new Exception(\"Test Exception\")));**");


        var prompt = promptBuilder.ToString();

        _logger.LogDebug("Generated prompt with {CharacterCount} characters for PR #{PrNumber}",
            prompt.Length, prContext.PullRequestNumber);

        return prompt;
    }

    private static string GetRegenerationSystemPrompt()
    {
        return @"You are an expert C# developer and unit testing specialist specializing in fixing compilation errors in unit tests.

Your task is to analyze a failing unit test and its compilation errors, then generate a corrected version that compiles successfully and it's parsable.

REQUIREMENTS:
1. Generate unit tests using NUnit framework with FluentAssertions
2. Fix ALL compilation errors identified in the error analysis
3. Maintain the original test intent and coverage goals
4. Use proper mocking with NSubstitute for dependencies
5. Follow AAA pattern (Arrange, Act, Assert)
6. Include descriptive test names that explain what is being tested
7. Consider async/await patterns where applicable
8. Address common compilation issues:
   - Missing using statements
   - Incorrect type references
   - Invalid method signatures
   - Improper mocking syntax
   - Namespace issues
   - Generic type constraints
   - Async/await misuse

RESPONSE FORMAT:
Return a JSON object with this structure:
{
  ""success"": true,
  ""generatedTests"": [
    {
      ""className"": ""ClassNameTests"",
      ""fileName"": ""ClassNameTests.cs"",
      ""testCode"": ""complete corrected C# test class code""
    }
  ]
}

COMPILATION ERROR ANALYSIS:
- Carefully analyze each compilation error
- Identify root causes (missing references, wrong types, syntax errors)
- Apply targeted fixes that resolve the specific errors
- Ensure fixes don't break other parts of the test
- Validate that all dependencies and namespaces are properly referenced

TESTING BEST PRACTICES:
- Test one thing per test method
- Use meaningful test data
- Mock external dependencies correctly
- Test both success and failure scenarios
- Verify exceptions are thrown when expected
- Test boundary conditions
- Use parameterized tests for multiple similar scenarios
- Ensure proper disposal of resources";
    }

    private string BuildTestRegenerationPrompt(GeneratedUnitTest failedTest, PullRequestContext originalPrContext)
    {
        var promptBuilder = new StringBuilder();

        promptBuilder.AppendLine("# Unit Test Compilation Error Analysis and Regeneration");
        promptBuilder.AppendLine();

        promptBuilder.AppendLine("## Failed Test Information");
        promptBuilder.AppendLine($"**Class Name**: {failedTest.ClassName}");
        promptBuilder.AppendLine($"**File Name**: {failedTest.FileName}");
        promptBuilder.AppendLine($"**Compilation Time**: {failedTest.CompilationResult?.CompilationTime}");
        promptBuilder.AppendLine();

        promptBuilder.AppendLine("## Original Context Summary");
        promptBuilder.AppendLine($"**Repository**: {originalPrContext.Repository}");
        promptBuilder.AppendLine($"**PR**: #{originalPrContext.PullRequestNumber} - {originalPrContext.Title}");

        if (!string.IsNullOrEmpty(failedTest.CompilationResult?.RepositoryContext))
        {
            promptBuilder.AppendLine($"**Repository Context**: {failedTest.CompilationResult.RepositoryContext}");
        }
        promptBuilder.AppendLine();

        promptBuilder.AppendLine("## Compilation Errors Analysis");
        promptBuilder.AppendLine($"**Total Errors**: {failedTest.CompilationResult?.Errors?.Count ?? 0}");
        promptBuilder.AppendLine();

        if (failedTest.CompilationResult?.Errors?.Any() == true)
        {
            var errorsByType = failedTest.CompilationResult.Errors
                .GroupBy(e => e.ErrorCode)
                .OrderByDescending(g => g.Count());

            promptBuilder.AppendLine("### Error Summary by Type:");
            foreach (var errorGroup in errorsByType)
            {
                promptBuilder.AppendLine($"- **{errorGroup.Key}**: {errorGroup.Count()} occurrence(s)");
            }
            promptBuilder.AppendLine();

            promptBuilder.AppendLine("### Detailed Error Analysis:");
            var sortedErrors = failedTest.CompilationResult.Errors
                .OrderBy(e => e.LineNumber)
                .ThenBy(e => e.ColumnNumber);

            foreach (var error in sortedErrors)
            {
                promptBuilder.AppendLine($"**Error {error.ErrorCode}** (Line {error.LineNumber}, Column {error.ColumnNumber})");
                promptBuilder.AppendLine($"- **Severity**: {error.Severity}");
                promptBuilder.AppendLine($"- **Message**: {error.Message}");
                promptBuilder.AppendLine($"- **Location**: Line {error.LineNumber}, Column {error.ColumnNumber}");

                promptBuilder.AppendLine();
            }
        }

        // Original Test Code
        promptBuilder.AppendLine("## Original Failing Test Code");
        promptBuilder.AppendLine("```csharp");
        promptBuilder.AppendLine(failedTest.TestCode);
        promptBuilder.AppendLine("```");
        promptBuilder.AppendLine();

        // AppendModifiedFilesContext(promptBuilder, originalPrContext);


        // Task instructions
        promptBuilder.AppendLine("## Task Instructions");
        promptBuilder.AppendLine("Please analyze the compilation errors above and generate a corrected version of the unit test that:");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("1. **Fixes all compilation errors** identified in the error analysis");
        promptBuilder.AppendLine("2. **Maintains the original test intent** and coverage objectives");
        promptBuilder.AppendLine("3. **Uses correct syntax and references** for all dependencies");
        promptBuilder.AppendLine("4. **Follows C# and NUnit best practices**");
        promptBuilder.AppendLine("5. **Includes proper using statements** and namespace declarations");
        promptBuilder.AppendLine("6. **Uses appropriate mocking techniques** with NSubstitute");
        promptBuilder.AppendLine("7. **Handles async/await patterns correctly** if applicable");
        promptBuilder.AppendLine("8. **Validates method signatures and return types**");
        promptBuilder.AppendLine("9. **Ensures proper generic type usage**");
        promptBuilder.AppendLine("10. **Addresses any missing or incorrect references**");
        promptBuilder.AppendLine("11. **Do not overcomplicate them, please give the response in a valid parsable json in the format I requested you by not adding characters that might affect parsing like '@'**");
        promptBuilder.AppendLine("12. **For async methods use this syntax _dependency.Method().Returns(Task.FromException<ReturnType>(new Exception(\"Test Exception\")));**");
        promptBuilder.AppendLine();

        var prompt = promptBuilder.ToString();

        _logger.LogDebug("Generated regeneration prompt with {CharacterCount} characters for test {ClassName}",
            prompt.Length, failedTest.ClassName);

        return prompt;
    }


    private UnitTestGenerationResult ParseResponse(string jsonResponse)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var response = JsonSerializer.Deserialize<GeminiTestResponse>(jsonResponse, options);

            if (response == null)
            {
                throw new InvalidOperationException("Failed to deserialize Gemini response");
            }

            return new UnitTestGenerationResult
            {
                Success = response.Success,
                GeneratedTests = response.GeneratedTests ?? new List<GeneratedUnitTest>(),
                ErrorMessage = string.Empty
            };
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Gemini JSON response: {Response}", jsonResponse);

            // Fallback: try to extract code blocks if JSON parsing fails
            var fallbackTests = ExtractCodeBlocksFallback(jsonResponse);

            return new UnitTestGenerationResult
            {
                Success = fallbackTests.Any(),
                GeneratedTests = fallbackTests,
                ErrorMessage = fallbackTests.Any() ? string.Empty : "Failed to parse response and extract code blocks"
            };
        }
    }

    private List<GeneratedUnitTest> ExtractCodeBlocksFallback(string response)
    {
        var tests = new List<GeneratedUnitTest>();
        var codeBlockPattern = @"```csharp\s*(.*?)\s*```";
        var matches = System.Text.RegularExpressions.Regex.Matches(response, codeBlockPattern,
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        for (int i = 0; i < matches.Count; i++)
        {
            var code = matches[i].Groups[1].Value.Trim();
            if (code.Contains("Test") && code.Contains("class"))
            {
                tests.Add(new GeneratedUnitTest
                {
                    ClassName = $"ExtractedTest{i + 1}",
                    FileName = $"ExtractedTest{i + 1}.cs",
                    TestCode = code,
                });
            }
        }

        return tests;
    }

    private static void AppendModifiedFilesContext(StringBuilder promptBuilder, PullRequestContext prContext, string generatedTestFileName = null)
    {
        promptBuilder.AppendLine("## Modified Files Analysis");

        foreach (var file in prContext.ModifiedFiles)
        {
            promptBuilder.AppendLine($"### File: {file.FileName}");
            promptBuilder.AppendLine($"- **Status**: {file.Status}");
            promptBuilder.AppendLine($"- **Changes**: {file.Changes} lines");
            promptBuilder.AppendLine();

            if (file.ExtractedClasses.Any())
            {
                promptBuilder.AppendLine("**Classes Modified/Added:**");
                foreach (var className in file.ExtractedClasses)
                {
                    promptBuilder.AppendLine($"- {className}");
                }
                promptBuilder.AppendLine();
            }

            if (file.ExtractedMethods.Any())
            {
                promptBuilder.AppendLine("**Methods Modified/Added:**");
                foreach (var method in file.ExtractedMethods)
                {
                    promptBuilder.AppendLine($"- {method}");
                }
                promptBuilder.AppendLine();
            }

            if (file.Dependencies.Any())
            {
                promptBuilder.AppendLine("**Dependencies:**");
                foreach (var dependency in file.Dependencies)
                {
                    promptBuilder.AppendLine($"- {dependency}");
                }
                promptBuilder.AppendLine();
            }

            if (!string.IsNullOrEmpty(file.RelevantContent))
            {
                if (file.Status == "added")
                {
                    promptBuilder.AppendLine("**Relevant Code Content:**");
                    promptBuilder.AppendLine("```csharp");
                    promptBuilder.AppendLine(file.RelevantContent);
                    promptBuilder.AppendLine("```");
                    promptBuilder.AppendLine();
                }
                else if (file.Status == "modified")
                {
                    promptBuilder.AppendLine("**Relevant Code Content:**");
                    promptBuilder.AppendLine("```csharp");
                    promptBuilder.AppendLine(file.RelevantContent);
                    promptBuilder.AppendLine("```");
                    promptBuilder.AppendLine();

                    promptBuilder.AppendLine("**Changes in PR(Patch):**");
                    promptBuilder.AppendLine("```csharp");
                    promptBuilder.AppendLine(file.Patch);
                    promptBuilder.AppendLine("```");
                    promptBuilder.AppendLine();
                }
            }
        }

        // Include related files context if available
        if (prContext.RelatedFiles.Any())
        {
            promptBuilder.AppendLine("## Related Files Context");
            foreach (var relatedFile in prContext.RelatedFiles.Take(3)) // Limit to avoid token overflow
            {
                promptBuilder.AppendLine($"### {relatedFile.FileName}");
                if (relatedFile.Dependencies.Any())
                {
                    promptBuilder.AppendLine("**Dependencies:** " + string.Join(", ", relatedFile.Dependencies));
                }
                if (!string.IsNullOrEmpty(relatedFile.RelevantContent))
                {
                    promptBuilder.AppendLine("```csharp");
                    promptBuilder.AppendLine(relatedFile.RelevantContent.Substring(0, Math.Min(500, relatedFile.RelevantContent.Length))); // Truncate for context
                    promptBuilder.AppendLine("```");
                }
                promptBuilder.AppendLine();
            }
        }
    }
}
