using AutonomousResearchAgent.Application.Admin;
using AutonomousResearchAgent.Application.Analysis;
using AutonomousResearchAgent.Application.Auth;
using AutonomousResearchAgent.Application.Cache;
using AutonomousResearchAgent.Application.Citations;
using AutonomousResearchAgent.Application.Chat;
using AutonomousResearchAgent.Application.Collections;
using AutonomousResearchAgent.Application.Documents;
using AutonomousResearchAgent.Application.Duplicates;
using AutonomousResearchAgent.Application.Jobs;
using AutonomousResearchAgent.Application.LiteratureReviews;
using AutonomousResearchAgent.Application.Papers;
using AutonomousResearchAgent.Application.ResearchGoals;
using AutonomousResearchAgent.Application.Search;
using AutonomousResearchAgent.Application.Summaries;
using AutonomousResearchAgent.Application.Users;
using AutonomousResearchAgent.Application.Watchlist;
using AutonomousResearchAgent.Infrastructure.BackgroundJobs;
using AutonomousResearchAgent.Infrastructure.Configuration;
using AutonomousResearchAgent.Infrastructure.External.OpenRouter;
using AutonomousResearchAgent.Infrastructure.External.SemanticScholar;
using AutonomousResearchAgent.Infrastructure.Persistence;
using AutonomousResearchAgent.Infrastructure.Services;
using AutonomousResearchAgent.Infrastructure.Services.Summaries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pgvector.EntityFrameworkCore;
using StackExchange.Redis;

namespace AutonomousResearchAgent.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");

        services.Configure<SemanticScholarOptions>(configuration.GetSection(SemanticScholarOptions.SectionName));
        services.Configure<OpenRouterOptions>(configuration.GetSection(OpenRouterOptions.SectionName));
        services.PostConfigure<OpenRouterOptions>(options =>
        {
            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                options.ApiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
            }
        });
        services.AddSingleton<IValidateOptions<OpenRouterOptions>, OpenRouterOptionsValidator>();
        services.AddSingleton<IValidateOptions<SemanticScholarOptions>, SemanticScholarOptionsValidator>();
        services.Configure<BackgroundJobOptions>(configuration.GetSection(BackgroundJobOptions.SectionName));
        services.Configure<DocumentProcessingOptions>(configuration.GetSection(DocumentProcessingOptions.SectionName));
        services.Configure<LocalEmbeddingOptions>(configuration.GetSection(LocalEmbeddingOptions.SectionName));
        services.Configure<SearchWeightsOptions>(configuration.GetSection(SearchWeightsOptions.SectionName));
        services.Configure<SummaryOptions>(configuration.GetSection(SummaryOptions.SectionName));
        services.Configure<CacheOptions>(configuration.GetSection("CacheOptions"));

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString, npgsqlOptions => npgsqlOptions.UseVector()));

        services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString, npgsqlOptions => npgsqlOptions.UseVector()));

        var redisConnectionString = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddSingleton<IConnectionMultiplexer>(sp =>
                ConnectionMultiplexer.Connect(redisConnectionString));
            services.AddScoped<ICacheService, RedisCacheService>();
        }
        else
        {
            services.AddSingleton<ICacheService, InMemoryCacheService>();
        }

        services.AddScoped<IPaperService, PaperService>();
        services.AddScoped<IPaperDocumentService, PaperDocumentService>();
        services.AddScoped<PaperDocumentProcessingService>();
        services.AddScoped<ISummaryService, SummaryService>();
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IAnalysisService, AnalysisService>();
        services.AddScoped<IJobRunner, AutonomousJobRunner>();
        services.AddScoped<IEmbeddingIndexingService, EmbeddingIndexingService>();
        services.AddScoped<IDocumentTextExtractor, LocalDocumentTextExtractor>();
        services.AddScoped<ITextChunkingService, RecursiveCharacterTextChunkingService>();
        services.AddScoped<ISummarizationService, OpenRouterSummarizationService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ICitationGraphService, CitationGraphService>();
        services.AddScoped<IResearchGoalService, ResearchGoalService>();
        services.AddScoped<IDuplicateDetectionService, DuplicateDetectionService>();
        services.Configure<DuplicateDetectionOptions>(configuration.GetSection(DuplicateDetectionOptions.SectionName));
        services.AddScoped<ICollectionService, CollectionService>();
        services.AddScoped<ILiteratureReviewService, LiteratureReviewService>();
        services.AddScoped<ISavedSearchService, SavedSearchService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IAnalyticsService, AnalyticsService>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddScoped<IPromptVersionService, PromptVersionService>();
        services.AddHostedService<DatabaseJobWorker>();

        services.Configure<VisionPdfExtractorOptions>(configuration.GetSection(VisionPdfExtractorOptions.SectionName));
        services.PostConfigure<VisionPdfExtractorOptions>(options =>
        {
            if (string.IsNullOrWhiteSpace(options.VisionApiKey))
            {
                options.VisionApiKey = Environment.GetEnvironmentVariable("VISION_API_KEY");
            }
        });
        services.AddHttpClient<VisionPdfExtractor>();
        services.AddScoped<VisionPdfExtractor>();

        services.AddHttpClient<ISemanticScholarClient, SemanticScholarClient>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SemanticScholarOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        services.AddHttpClient<OpenRouterChatClient>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpenRouterOptions>>().Value;
            var baseUrl = options.BaseUrl.TrimEnd('/') + "/";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        services.AddHttpClient<LocalEmbeddingHttpClient>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<LocalEmbeddingOptions>>().Value;
            var baseUrl = options.BaseUrl.TrimEnd('/') + "/";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });
        services.AddTransient<ILocalEmbeddingClient>(serviceProvider => serviceProvider.GetRequiredService<LocalEmbeddingHttpClient>());
        services.AddTransient<IEmbeddingService>(serviceProvider => serviceProvider.GetRequiredService<LocalEmbeddingHttpClient>());

        var documentProcessingOptions = configuration.GetSection(DocumentProcessingOptions.SectionName).Get<DocumentProcessingOptions>() ?? new DocumentProcessingOptions();
        services.AddHttpClient("PaperDocuments", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(documentProcessingOptions.DownloadTimeoutSeconds);
        });

        services.AddHttpClient("Webhooks");

        return services;
    }
}

internal sealed class InMemoryCacheService : ICacheService
{
    private readonly Dictionary<string, (string Value, DateTimeOffset? Expiry)> _cache = new();
    private readonly object _lock = new();

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                if (entry.Expiry == null || entry.Expiry > DateTimeOffset.UtcNow)
                {
                    return Task.FromResult<T?>(System.Text.Json.JsonSerializer.Deserialize<T>(entry.Value));
                }
                _cache.Remove(key);
            }
        }
        return Task.FromResult<T?>(null);
    }

    public Task<bool> SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default) where T : class
    {
        var serialized = System.Text.Json.JsonSerializer.Serialize(value);
        return SetAsync(key, serialized, ttl, cancellationToken);
    }

    public Task<bool> SetAsync(string key, string value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var expiry = ttl.HasValue ? DateTimeOffset.UtcNow.Add(ttl.Value) : (DateTimeOffset?)null;
            _cache[key] = (value, expiry);
        }
        return Task.FromResult(true);
    }

    public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_cache.Remove(key));
        }
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                if (entry.Expiry == null || entry.Expiry > DateTimeOffset.UtcNow)
                {
                    return Task.FromResult(true);
                }
                _cache.Remove(key);
            }
        }
        return Task.FromResult(false);
    }

    public Task<bool> SetExpirationAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                _cache[key] = (entry.Value, DateTimeOffset.UtcNow.Add(ttl));
                return Task.FromResult(true);
            }
        }
        return Task.FromResult(false);
    }

    public async Task<T?> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? ttl = null, CancellationToken cancellationToken = default) where T : class
    {
        var cached = await GetAsync<T>(key, cancellationToken);
        if (cached != null)
        {
            return cached;
        }

        var value = await factory();
        if (value != null)
        {
            await SetAsync(key, value, ttl, cancellationToken);
        }

        return value;
    }
}
