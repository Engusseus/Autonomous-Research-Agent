namespace AutonomousResearchAgent.Infrastructure.Services;

public sealed class DocumentProcessingOptions
{
    public const string SectionName = "DocumentProcessing";

    public string StorageRoot { get; set; } = "data/paper-documents";
    public int MaxDownloadSizeMegabytes { get; set; } = 50;
    public string OcrExecutablePath { get; set; } = "ocrmypdf";
    public int OcrFallbackMinimumCharacters { get; set; } = 32;
}
