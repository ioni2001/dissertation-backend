using dissertation_backend.Services.Interfaces;
using Models.GithubModels.PatchModels;

namespace dissertation_backend.Services.Implementations
{
    public class PatchMergerService : IPatchMergerService
    {
        private readonly ILogger<PatchMergerService> _logger;

        public PatchMergerService(ILogger<PatchMergerService> logger)
        {
            _logger = logger;
        }

        public string MergePatchWithContent(string originalContent, string patch)
        {
            if (string.IsNullOrEmpty(originalContent))
                throw new ArgumentException("Original content cannot be null or empty", nameof(originalContent));

            if (string.IsNullOrEmpty(patch))
            {
                _logger.LogWarning("Patch is empty, returning original content");
                return originalContent;
            }

            var originalLines = originalContent.Split(new[] { '\n', '\r' }, StringSplitOptions.None)
                .Where(line => !string.IsNullOrEmpty(line) || originalContent.Contains(line))
                .ToList();

            var patchHunks = ParsePatch(patch);
            var result = new List<string>(originalLines);

            // Apply hunks in reverse order to maintain line number integrity
            foreach (var hunk in patchHunks.OrderByDescending(h => h.OriginalStartLine))
            {
                result = ApplyHunk(result, hunk);
            }

            return string.Join(Environment.NewLine, result);
        }

        /// <summary>
        /// Parses the patch string into structured hunk data
        /// </summary>
        private List<PatchHunk> ParsePatch(string patch)
        {
            var hunks = new List<PatchHunk>();
            var lines = patch.Split('\n');
            PatchHunk? currentHunk = null;

            foreach (var line in lines)
            {
                // Check for hunk header (e.g., @@ -1,4 +1,6 @@)
                if (line.StartsWith("@@"))
                {
                    if (currentHunk != null)
                        hunks.Add(currentHunk);

                    currentHunk = ParseHunkHeader(line);
                }
                else if (currentHunk != null)
                {
                    // Parse hunk content lines
                    if (line.StartsWith("-"))
                    {
                        // Line removed
                        currentHunk.Changes.Add(new PatchChange
                        {
                            Type = ChangeType.Removed,
                            Content = line.Substring(1) // Remove the '-' prefix
                        });
                    }
                    else if (line.StartsWith("+"))
                    {
                        // Line added
                        currentHunk.Changes.Add(new PatchChange
                        {
                            Type = ChangeType.Added,
                            Content = line.Substring(1) // Remove the '+' prefix
                        });
                    }
                    else if (line.StartsWith(" ") || string.IsNullOrEmpty(line))
                    {
                        // Context line (unchanged)
                        currentHunk.Changes.Add(new PatchChange
                        {
                            Type = ChangeType.Context,
                            Content = line.Length > 0 ? line.Substring(1) : line
                        });
                    }
                }
            }

            if (currentHunk != null)
                hunks.Add(currentHunk);

            return hunks;
        }

        /// <summary>
        /// Parses a hunk header line to extract line number information
        /// </summary>
        private PatchHunk ParseHunkHeader(string headerLine)
        {
            // Example: @@ -1,4 +1,6 @@
            var regex = new System.Text.RegularExpressions.Regex(@"@@\s*-(\d+)(?:,(\d+))?\s*\+(\d+)(?:,(\d+))?\s*@@");
            var match = regex.Match(headerLine);

            if (!match.Success)
                throw new InvalidOperationException($"Invalid hunk header format: {headerLine}");

            return new PatchHunk
            {
                OriginalStartLine = int.Parse(match.Groups[1].Value) - 1, // Convert to 0-based indexing
                OriginalLineCount = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 1,
                NewStartLine = int.Parse(match.Groups[3].Value) - 1, // Convert to 0-based indexing
                NewLineCount = match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : 1,
                Changes = new List<PatchChange>()
            };
        }

        /// <summary>
        /// Applies a single hunk to the content
        /// </summary>
        private List<string> ApplyHunk(List<string> content, PatchHunk hunk)
        {
            var result = new List<string>(content);
            var originalIndex = hunk.OriginalStartLine;
            var changeIndex = 0;
            var linesToRemove = new List<int>();
            var linesToAdd = new List<(int index, string content)>();

            _logger.LogDebug("Applying hunk starting at line {StartLine} with {ChangeCount} changes",
                hunk.OriginalStartLine + 1, hunk.Changes.Count);

            // Process each change in the hunk
            foreach (var change in hunk.Changes)
            {
                switch (change.Type)
                {
                    case ChangeType.Context:
                        // Context lines help us verify we're at the right position
                        if (originalIndex < result.Count)
                        {
                            var originalLine = result[originalIndex].TrimEnd();
                            var contextLine = change.Content.TrimEnd();

                            if (originalLine != contextLine)
                            {
                                _logger.LogWarning("Context mismatch at line {LineNumber}. Expected: '{Expected}', Found: '{Found}'",
                                    originalIndex + 1, contextLine, originalLine);
                            }
                        }
                        originalIndex++;
                        break;

                    case ChangeType.Removed:
                        // Mark line for removal
                        if (originalIndex < result.Count)
                        {
                            linesToRemove.Add(originalIndex);
                            _logger.LogDebug("Marking line {LineNumber} for removal: '{Content}'",
                                originalIndex + 1, change.Content);
                        }
                        originalIndex++;
                        break;

                    case ChangeType.Added:
                        // Mark line for addition at current position
                        linesToAdd.Add((originalIndex, change.Content));
                        _logger.LogDebug("Marking line for addition at position {Position}: '{Content}'",
                            originalIndex + 1, change.Content);
                        break;
                }
            }

            // Apply removals (in reverse order to maintain indices)
            foreach (var indexToRemove in linesToRemove.OrderByDescending(i => i))
            {
                if (indexToRemove < result.Count)
                {
                    result.RemoveAt(indexToRemove);
                }
            }

            // Adjust insertion indices after removals
            var adjustedInsertions = new List<(int index, string content)>();
            foreach (var (index, content1) in linesToAdd)
            {
                var adjustedIndex = index;

                var removedBefore = linesToRemove.Count(r => r < index);
                adjustedIndex -= removedBefore;

                adjustedInsertions.Add((Math.Max(0, Math.Min(adjustedIndex, result.Count)), content1));
            }

            ApplyInsertions(result, adjustedInsertions);

            return result;
        }

        private void ApplyInsertions(List<string> result, List<(int index, string content)> insertions)
        {
            if (!insertions.Any()) return;

            // Group insertions by their target index
            var insertionGroups = insertions
                .GroupBy(i => i.index)
                .OrderBy(g => g.Key) // Process groups from low to high index
                .ToList();

            var cumulativeOffset = 0;

            foreach (var group in insertionGroups)
            {
                var targetIndex = group.Key;
                var actualInsertIndex = targetIndex + cumulativeOffset;

                // Ensure we don't insert beyond the list bounds
                actualInsertIndex = Math.Min(actualInsertIndex, result.Count);

                var groupInsertions = group.ToList();

                _logger.LogDebug("Inserting {Count} lines at original index {OriginalIndex} (actual index {ActualIndex})",
                    groupInsertions.Count, targetIndex, actualInsertIndex);

                // Insert all lines for this group at the same logical position
                // Insert in order so they appear in the correct sequence
                for (int i = 0; i < groupInsertions.Count; i++)
                {
                    var (_, content) = groupInsertions[i];
                    result.Insert(actualInsertIndex + i, content);

                    _logger.LogDebug("Inserted line at position {Position}: '{Content}'",
                        actualInsertIndex + i, content);
                }

                // Update cumulative offset for subsequent groups
                cumulativeOffset += groupInsertions.Count;
            }
        }
    }
}
