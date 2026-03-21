using System.Net;
using System.Net.Sockets;
using System.ComponentModel;
using System.Diagnostics;
using AutonomousResearchAgent.Application.Common;
using AutonomousResearchAgent.Domain.Entities;
using AutonomousResearchAgent.Domain.Enums;
using AutonomousResearchAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutonomousResearchAgent.Infrastructure.Services;

public sealed class PaperDocumentProcessingService(
    ApplicationDbContext dbContext,
    IHttpClientFactory httpClientFactory,
    IHostEnvironment hostEnvironment,
    IOptions<DocumentProcessingOptions> options,
    ILogger<PaperDocumentProcessingService> logger,
    IDocumentTextExtractor? textExtractor = null)
{
    private readonly DocumentProcessingOptions _options = options.Value;
    private readonly IDocumentTextExtractor _textExtractor = textExtractor ?? new LocalDocumentTextExtractor();

    public async Task<PaperDocument> ProcessAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await dbContext.PaperDocuments.FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken)
            ?? throw new NotFoundException(nameof(PaperDocument), documentId);

        try
        {
            var sourceUri = await ValidateSourceUriAsync(document.SourceUrl, cancellationToken);

            using var client = httpClientFactory.CreateClient("PaperDocuments");
            using var response = await client.GetAsync(sourceUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var maxDownloadBytes = _options.MaxDownloadSizeMegabytes * 1024L * 1024L;
            if (response.Content.Headers.ContentLength is > 0 && response.Content.Headers.ContentLength > maxDownloadBytes)
            {
                throw new InvalidStateException($"Document exceeds max allowed size of {_options.MaxDownloadSizeMegabytes} MB.");
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType ?? document.MediaType ?? GuessMediaType(document.SourceUrl, document.FileName);
            var fileName = document.FileName ?? DeriveFileName(document.SourceUrl, mediaType, document.Id);
            var bytes = await ReadBytesWithLimitAsync(response.Content, maxDownloadBytes, cancellationToken);

            var storageRoot = Path.IsPathRooted(_options.StorageRoot)
                ? _options.StorageRoot
                : Path.Combine(hostEnvironment.ContentRootPath, _options.StorageRoot);
            Directory.CreateDirectory(storageRoot);

            var targetDirectory = Path.Combine(storageRoot, document.PaperId.ToString("N"));
            Directory.CreateDirectory(targetDirectory);

            var targetPath = Path.Combine(targetDirectory, $"{document.Id:N}{Path.GetExtension(fileName)}");
            await File.WriteAllBytesAsync(targetPath, bytes, cancellationToken);

            document.FileName = fileName;
            document.MediaType = mediaType;
            document.StoragePath = targetPath;
            document.DownloadedAt = DateTimeOffset.UtcNow;
            document.Status = PaperDocumentStatus.Downloaded;
            document.LastError = null;

            var nativeText = await _textExtractor.ExtractAsync(bytes, mediaType, fileName, cancellationToken);
            var shouldRunOcr = document.RequiresOcr || IsTooWeak(nativeText);
            if (shouldRunOcr)
            {
                var ocrText = await ExtractWithOcrAsync(targetPath, cancellationToken);
                if (string.IsNullOrWhiteSpace(ocrText))
                {
                    throw new InvalidStateException("Local OCR did not produce any text.");
                }

                document.ExtractedText = ocrText.Trim();
                document.ExtractedAt = DateTimeOffset.UtcNow;
                document.Status = PaperDocumentStatus.Extracted;
            }
            else
            {
                document.ExtractedText = nativeText!.Trim();
                document.ExtractedAt = DateTimeOffset.UtcNow;
                document.Status = PaperDocumentStatus.Extracted;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Processed paper document {DocumentId} with status {Status}", document.Id, document.Status);
            return document;
        }
        catch (Exception ex) when (ex is not NotFoundException)
        {
            document.Status = PaperDocumentStatus.Failed;
            document.ExtractedText = null;
            document.ExtractedAt = null;
            document.LastError = QueryHelpers.Truncate(ex.Message, 4096);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception saveException)
            {
                logger.LogWarning(saveException, "Failed to persist failure state for paper document {DocumentId}", document.Id);
            }

            throw;
        }
    }

    private static async Task<byte[]> ReadBytesWithLimitAsync(HttpContent content, long maxBytes, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var initialCapacity = content.Headers.ContentLength is > 0 and <= int.MaxValue
            ? (int)content.Headers.ContentLength.Value
            : 16 * 1024;
        using var buffer = new MemoryStream(initialCapacity);
        var chunk = new byte[16 * 1024];
        long totalRead = 0;

        while (true)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
            if (totalRead > maxBytes)
            {
                throw new InvalidStateException($"Document exceeds max allowed size of {maxBytes / 1024L / 1024L} MB.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static async Task<Uri> ValidateSourceUriAsync(string sourceUrl, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidStateException("Document source URL must be an absolute http or https URL.");
        }

        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidStateException("Document source URL must not target localhost.");
        }

        var addresses = IPAddress.TryParse(uri.Host, out var address)
            ? [address]
            : await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken);

        if (addresses.Length == 0 || addresses.Any(a => !IsPublicAddress(a)))
        {
            throw new InvalidStateException("Document source URL must resolve to a public internet address.");
        }

        return uri;
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return !address.IsIPv6LinkLocal && !address.IsIPv6Multicast && !address.IsIPv6SiteLocal && !address.IsIPv6Teredo;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] switch
        {
            10 => false,
            127 => false,
            169 when bytes[1] == 254 => false,
            172 when bytes[1] >= 16 && bytes[1] <= 31 => false,
            192 when bytes[1] == 168 => false,
            _ => true
        };
    }

    private bool IsTooWeak(string? extractedText)
    {
        return string.IsNullOrWhiteSpace(extractedText) || extractedText.Trim().Length < _options.OcrFallbackMinimumCharacters;
    }

    private async Task<string?> ExtractWithOcrAsync(string inputPath, CancellationToken cancellationToken)
    {
        var isPdf = string.Equals(Path.GetExtension(inputPath), ".pdf", StringComparison.OrdinalIgnoreCase);
        var tempDirectory = Path.Combine(Path.GetTempPath(), "ara-ocr", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var sidecarPath = Path.Combine(tempDirectory, "ocr.txt");
        var outputPath = Path.Combine(tempDirectory, "ocr.pdf");

        var startInfo = new ProcessStartInfo
        {
            FileName = _options.OcrExecutablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (isPdf)
        {
            startInfo.ArgumentList.Add("--force-ocr");
            startInfo.ArgumentList.Add("--sidecar");
            startInfo.ArgumentList.Add(sidecarPath);
            startInfo.ArgumentList.Add(inputPath);
            startInfo.ArgumentList.Add(outputPath);
        }
        else
        {
            startInfo.ArgumentList.Add(inputPath);
            startInfo.ArgumentList.Add("stdout");
        }

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidStateException($"Local OCR executable '{_options.OcrExecutablePath}' could not be started.");

            using var cancellationRegistration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Best-effort cleanup only.
                }
            });

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(cancellationToken);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                var errorMessage = string.IsNullOrWhiteSpace(stderr)
                    ? $"Local OCR executable '{_options.OcrExecutablePath}' failed with exit code {process.ExitCode}."
                    : $"Local OCR executable '{_options.OcrExecutablePath}' failed with exit code {process.ExitCode}: {stderr.Trim()}";

                throw new InvalidStateException(errorMessage);
            }

            if (File.Exists(sidecarPath))
            {
                var sidecarText = await File.ReadAllTextAsync(sidecarPath, cancellationToken);
                if (!string.IsNullOrWhiteSpace(sidecarText))
                {
                    return sidecarText.Trim();
                }
            }

            return string.IsNullOrWhiteSpace(stdout) ? null : stdout.Trim();
        }
        catch (InvalidStateException)
        {
            throw;
        }
        catch (Win32Exception ex)
        {
            throw new InvalidStateException($"Local OCR executable '{_options.OcrExecutablePath}' could not be started: {ex.Message}");
        }
        catch (Exception ex)
        {
            throw new InvalidStateException($"Local OCR executable '{_options.OcrExecutablePath}' failed unexpectedly: {ex.Message}");
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
            catch
            {
                // Best-effort temp cleanup only.
            }
        }
    }

    private static string DeriveFileName(string sourceUrl, string? mediaType, Guid documentId)
    {
        var uri = new Uri(sourceUrl);
        var fileName = Path.GetFileName(uri.LocalPath);
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            return fileName;
        }

        var extension = string.Equals(mediaType, "application/pdf", StringComparison.OrdinalIgnoreCase) ? ".pdf" : ".bin";
        return $"{documentId:N}{extension}";
    }

    private static string GuessMediaType(string sourceUrl, string? fileName)
    {
        var extension = Path.GetExtension(fileName ?? sourceUrl).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            ".md" => "text/markdown",
            ".json" => "application/json",
            _ => "application/octet-stream"
        };
    }

}
