using BusinessCard.Application.BusinessCards.Llm;
using BusinessCard.Application.Contacts;
using BusinessCard.Infrastructure.Graph;
using BusinessCard.Infrastructure.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BusinessCard.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Health-check tag shared with the API's readiness endpoint.</summary>
    public const string ReadinessTag = "ready";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        AddGraph(services, configuration);

        services.AddOptions<AiOptions>()
            .Bind(configuration.GetSection(AiOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AiOptions>, AiOptionsValidator>();

        var ai = configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();
        if (ai.Provider == AiProvider.Disabled)
        {
            services.AddSingleton<IBusinessCardLlmClassifier, DisabledLlmClassifier>();
        }
        else
        {
            services.AddHttpClient<IBusinessCardLlmClassifier, PhiBusinessCardLlmClassifier>(ConfigureLlmClient)
                // Keep-alive connections are reused across scans (no TLS/TCP setup per request);
                // the lifetime still lets DNS changes take effect.
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                    ConnectTimeout = TimeSpan.FromMilliseconds(ai.TimeoutMilliseconds),
                })
                .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
        }

        services.AddHttpClient(LlmHealthCheck.HttpClientName, (sp, client) =>
        {
            ConfigureLlmClient(sp, client);
            client.Timeout = TimeSpan.FromSeconds(2);
        });
        services.AddHealthChecks().AddCheck<LlmHealthCheck>("llm", tags: [ReadinessTag]);

        return services;
    }

    /// <remarks>
    /// The OBO token provider (<c>IAuthorizationHeaderProvider</c>) is registered by the API's
    /// authentication setup (<c>EnableTokenAcquisitionToCallDownstreamApi</c>).
    /// </remarks>
    private static void AddGraph(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GraphOptions>()
            .Bind(configuration.GetSection(GraphOptions.SectionName))
            .Validate(o => o.BaseUrl is { IsAbsoluteUri: true, Scheme: "https" }, "MicrosoftGraph:BaseUrl must be an absolute https URL.")
            .Validate(o => o.Scopes is { Length: > 0 }, "MicrosoftGraph:Scopes must list at least one delegated scope.")
            .Validate(o => o.TimeoutSeconds is >= 1 and <= 60, "MicrosoftGraph:TimeoutSeconds must be between 1 and 60.")
            .Validate(o => o.MaxInlineRetryDelaySeconds is >= 0 and <= 10, "MicrosoftGraph:MaxInlineRetryDelaySeconds must be between 0 and 10.")
            .ValidateOnStart();

        services.AddHttpClient<IMicrosoftGraphContactService, GraphContactService>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<GraphOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl.AbsoluteUri.TrimEnd('/') + "/");
                // GraphContactService enforces its own per-request timeout.
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
    }

    private static void ConfigureLlmClient(IServiceProvider services, HttpClient client)
    {
        var options = services.GetRequiredService<IOptions<AiOptions>>().Value;
        if (options.BaseUrl is { } baseUrl)
        {
            // Trailing slash so relative paths ("v1/chat/completions") append instead of replace.
            client.BaseAddress = new Uri(baseUrl.AbsoluteUri.TrimEnd('/') + "/");
        }
        // The classifier enforces its own per-request budget with a CancellationToken.
        client.Timeout = Timeout.InfiniteTimeSpan;
    }
}
