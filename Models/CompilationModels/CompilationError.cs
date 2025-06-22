namespace Models.CompilationModels;

public class CompilationError
{
    public string ErrorCode { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public int LineNumber { get; set; }

    public int ColumnNumber { get; set; }

    public string Severity { get; set; } = string.Empty;
}
