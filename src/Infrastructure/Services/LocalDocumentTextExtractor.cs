using System.Text;
using UglyToad.PdfPig;

namespace AutonomousResearchAgent.Infrastructure.Services;

public sealed class LocalDocumentTextExtractor : IDocumentTextExtractor
{
    public Task<string?> ExtractAsync(byte[] bytes, string? mediaType, string fileName, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        if (mediaType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) == true ||
            extension is ".txt" or ".md" or ".json" or ".csv")
        {
            return Task.FromResult<string?>(Encoding.UTF8.GetString(bytes));
        }

        if (extension == ".pdf" || string.Equals(mediaType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = new MemoryStream(bytes);
            using var pdf = PdfDocument.Open(stream);
            var text = string.Join("\n\n", pdf.GetPages().Select(page => page.Text).Where(pageText => !string.IsNullOrWhiteSpace(pageText))).Trim();
            return Task.FromResult<string?>(string.IsNullOrWhiteSpace(text) ? null : text);
        }

        return Task.FromResult<string?>(null);
    }
}
