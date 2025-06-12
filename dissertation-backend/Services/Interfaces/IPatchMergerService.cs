namespace dissertation_backend.Services.Interfaces;

public interface IPatchMergerService
{
    string MergePatchWithContent(string originalContent, string patch);
}
