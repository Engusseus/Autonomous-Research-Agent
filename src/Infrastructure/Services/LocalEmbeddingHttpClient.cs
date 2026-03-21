using System.Net.Http.Json;
using System.Text.Json;
using AutonomousResearchAgent.Application.Common;
using AutonomousResearchAgent.Application.Search;
using AutonomousResearchAgent.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutonomousResearchAgent.Infrastructure.Services;

public sealed class LocalEmbeddingHttpClient(
    HttpClient httpClient,
    IOptions<LocalEmbeddingOptions> options,
    ILogger<LocalEmbeddingHttpClient> logger) : ILocalEmbeddingClient, IEmbeddingService
{
    private readonly LocalEmbeddingOptions _options = options.Value;

    public async Task<float[]> GenerateEmbeddingAsync(string content, CancellationToken cancellationToken)
        => await GenerateEmbeddingInternalAsync(content, "document", cancellationToken);

    public async Task<float[]> GenerateEmbeddingAsync(string content, EmbeddingType embeddingType, CancellationToken cancellationToken)
        => await GenerateEmbeddingInternalAsync(content, "document", cancellationToken);

    public async Task<float[]> GenerateQueryEmbeddingAsync(string query, CancellationToken cancellationToken)
        => await GenerateEmbeddingInternalAsync(query, "query", cancellationToken);

    private async Task<float[]> GenerateEmbeddingInternalAsync(string content, string inputType, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Content must not be empty.", nameof(content));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.EmbeddingsPath)
        {
            Content = JsonContent.Create(new
            {
                model = _options.ModelName,
                input = content,
                input_type = inputType
            })
        };

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Local embedding service returned {StatusCode}. Body: {Body}", response.StatusCode, payload);
                response.EnsureSuccessStatusCode();
            }

            var vector = ParseEmbedding(payload);
            ValidateDimensions(vector);
            return vector;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Local embedding request failed.");
            throw new ExternalDependencyException("Local embedding request failed.", ex);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Local embedding service returned an invalid payload.");
            throw new ExternalDependencyException("Local embedding service returned an invalid payload.", ex);
        }
    }

    private static float[] ParseEmbedding(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            return ReadVector(root);
        }

        if (root.TryGetProperty("embedding", out var embedding))
        {
            return ReadVector(embedding);
        }

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
        {
            var first = data[0];
            if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("embedding", out var itemEmbedding))
            {
                return ReadVector(itemEmbedding);
            }
        }

        throw new JsonException("Embedding payload did not contain a vector.");
    }

    private void ValidateDimensions(float[] vector)
    {
        if (_options.VectorDimensions <= 0 || vector.Length == _options.VectorDimensions)
        {
            return;
        }

        logger.LogError(
            "Local embedding service returned vector with {ActualDimensions} dimensions, expected {ExpectedDimensions}.",
            vector.Length,
            _options.VectorDimensions);

        throw new ExternalDependencyException(
            $"Local embedding service returned vector with unexpected dimensions: expected {_options.VectorDimensions}, got {vector.Length}.");
    }

    private static float[] ReadVector(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Embedding payload did not contain a vector array.");
        }

        var vector = new float[element.GetArrayLength()];
        var index = 0;

        foreach (var value in element.EnumerateArray())
        {
            vector[index++] = value.GetSingle();
        }

        return vector;
    }
}
