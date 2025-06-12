using dissertation_backend.Services.Interfaces;
using System.Text.RegularExpressions;
using System.Text;
using Models.GithubModels.ContextModels;

namespace dissertation_backend.Services.Implementations;

public class CodeAnalysisService : ICodeAnalysisService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<CodeAnalysisService> _logger;

    public CodeAnalysisService(IConfiguration configuration, ILogger<CodeAnalysisService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public AnalyzedCode AnalyzeAndTruncateCode(string content, string patch)
    {
        var result = new AnalyzedCode();

        // Extract basic information
        result.Classes = ExtractClasses(content);
        result.Methods = ExtractMethods(content);
        result.Dependencies = ExtractDependencies(content);

        // Get changed line numbers from patch
        var changedLines = ExtractChangedLines(patch);

        // Apply truncation heuristics
        result.TruncatedContent = ApplyTruncationHeuristics(content, changedLines);
        result.EstimatedTokenCount = EstimateTokenCount(result.TruncatedContent);

        return result;
    }

    private List<string> ExtractClasses(string content)
    {
        var classes = new List<string>();
        var classPattern = @"(?:public|private|protected|internal)?\s*(?:static\s+)?(?:abstract\s+)?(?:sealed\s+)?class\s+(\w+)";
        var matches = Regex.Matches(content, classPattern, RegexOptions.Multiline);

        foreach (Match match in matches)
        {
            if (match.Groups.Count > 1)
            {
                classes.Add(match.Groups[1].Value);
            }
        }

        return classes;
    }

    private List<string> ExtractMethods(string content)
    {
        var methods = new List<string>();
        var methodPattern = @"(?:public|private|protected|internal)\s+(?:static\s+)?(?:virtual\s+)?(?:override\s+)?(?:async\s+)?(?:\w+(?:<[^>]+>)?)\s+(\w+)\s*\([^)]*\)";
        var matches = Regex.Matches(content, methodPattern, RegexOptions.Multiline);

        foreach (Match match in matches)
        {
            if (match.Groups.Count > 1)
            {
                methods.Add(match.Groups[1].Value);
            }
        }

        return methods;
    }

    public List<string> ExtractDependencies(string content)
    {
        var dependencies = new HashSet<string>();

        // Extract using statements
        var usingPattern = @"using\s+([^;]+);";
        var usingMatches = Regex.Matches(content, usingPattern);
        foreach (Match match in usingMatches)
        {
            dependencies.Add(match.Groups[1].Value.Trim());
        }

        // Extract type references in the code
        var typePattern = @"\b([A-Z][a-zA-Z0-9_]*(?:<[^>]+>)?)\s+\w+\s*[=;]";
        var typeMatches = Regex.Matches(content, typePattern);
        foreach (Match match in typeMatches)
        {
            dependencies.Add(match.Groups[1].Value);
        }

        // Extract interface implementations
        var interfacePattern = @":\s*([I][A-Z][a-zA-Z0-9_]*)";
        var interfaceMatches = Regex.Matches(content, interfacePattern);
        foreach (Match match in interfaceMatches)
        {
            dependencies.Add(match.Groups[1].Value);
        }

        return dependencies.ToList();
    }

    private HashSet<int> ExtractChangedLines(string patch)
    {
        var changedLines = new HashSet<int>();

        if (string.IsNullOrEmpty(patch)) return changedLines;

        var lines = patch.Split('\n');
        var currentLine = 0;

        foreach (var line in lines)
        {
            if (line.StartsWith("@@"))
            {
                // Extract line number from hunk header
                var match = Regex.Match(line, @"\+(\d+)");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var lineNum))
                {
                    currentLine = lineNum;
                }
            }
            else if (line.StartsWith("+") || line.StartsWith("-"))
            {
                changedLines.Add(currentLine);
                if (line.StartsWith("+") && !line.StartsWith("+++"))
                {
                    currentLine++;
                }
            }
            else if (!line.StartsWith("---") && !line.StartsWith("+++"))
            {
                currentLine++;
            }
        }

        return changedLines;
    }

    private string ApplyTruncationHeuristics(string content, HashSet<int> changedLines)
    {
        var maxTokens = _configuration.GetValue<int>("OpenAI:MaxTokensPerFile", 2000);
        var currentTokens = EstimateTokenCount(content);

        if (currentTokens <= maxTokens)
        {
            return content;
        }

        var lines = content.Split('\n');
        var prioritizedLines = new List<(int index, string line, int priority)>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var priority = CalculateLinePriority(line, i, changedLines);
            prioritizedLines.Add((i, line, priority));
        }

        // Sort by priority and include lines until we reach token limit
        var selectedLines = new Dictionary<int, string>();
        var currentEstimate = 0;

        foreach (var (index, line, priority) in prioritizedLines.OrderByDescending(x => x.priority))
        {
            var lineTokens = EstimateTokenCount(line);
            if (currentEstimate + lineTokens <= maxTokens)
            {
                selectedLines[index] = line;
                currentEstimate += lineTokens;
            }
        }

        // Reconstruct content maintaining original order
        var result = new StringBuilder();
        var sortedIndexes = selectedLines.Keys.OrderBy(x => x).ToList();

        for (int i = 0; i < sortedIndexes.Count; i++)
        {
            var index = sortedIndexes[i];

            // Add context markers for gaps
            if (i > 0 && index - sortedIndexes[i - 1] > 1)
            {
                result.AppendLine("// ... [Code truncated for brevity] ...");
            }

            result.AppendLine(selectedLines[index]);
        }

        return result.ToString();
    }

    private int CalculateLinePriority(string line, int lineIndex, HashSet<int> changedLines)
    {
        var priority = 0;
        var trimmedLine = line.Trim();

        // Highest priority for changed lines
        if (changedLines.Contains(lineIndex + 1))
        {
            priority += 100;
        }

        // High priority for method signatures
        if (Regex.IsMatch(trimmedLine, @"(public|private|protected|internal)\s+.*\s+\w+\s*\("))
        {
            priority += 80;
        }

        // High priority for class declarations
        if (Regex.IsMatch(trimmedLine, @"(public|private|protected|internal)\s+class\s+\w+"))
        {
            priority += 90;
        }

        // Medium priority for property declarations
        if (Regex.IsMatch(trimmedLine, @"(public|private|protected|internal)\s+.*\s+\w+\s*{\s*(get|set)"))
        {
            priority += 60;
        }

        // Medium priority for using statements
        if (trimmedLine.StartsWith("using "))
        {
            priority += 50;
        }

        // Medium priority for interface implementations
        if (trimmedLine.Contains(": I") && trimmedLine.Contains("class"))
        {
            priority += 70;
        }

        // Lower priority for comments
        if (trimmedLine.StartsWith("//") || trimmedLine.StartsWith("/*"))
        {
            priority += 10;
        }

        // Very low priority for empty lines
        if (string.IsNullOrWhiteSpace(trimmedLine))
        {
            priority += 1;
        }

        // Context priority - lines near changed lines get bonus
        foreach (var changedLine in changedLines)
        {
            var distance = Math.Abs(lineIndex + 1 - changedLine);
            if (distance <= 3)
            {
                priority += Math.Max(0, 30 - distance * 10);
            }
        }

        return priority;
    }

    public int EstimateTokenCount(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        // Rough estimation: 1 token ≈ 4 characters for code
        // This is a simplification - actual tokenization depends on the model
        var characterCount = text.Length;
        var wordCount = text.Split(new char[] { ' ', '\n', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;

        // Use a conservative estimate
        return Math.Max(characterCount / 3, wordCount);
    }
}