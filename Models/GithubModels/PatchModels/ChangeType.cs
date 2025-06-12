namespace Models.GithubModels.PatchModels;

public enum ChangeType
{
    Context,  // Unchanged line (prefix: ' ')
    Added,    // Added line (prefix: '+')
    Removed   // Removed line (prefix: '-')
}
