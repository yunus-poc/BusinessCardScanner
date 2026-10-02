using BusinessCard.Application.BusinessCards;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessCard.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        // Validators are stateless; singletons avoid re-building rule sets per request.
        services.AddValidatorsFromAssemblyContaining<ApplicationAssemblyMarker>(ServiceLifetime.Singleton, includeInternalTypes: true);

        services.AddOptions<ParsingOptions>()
            .Bind(configuration.GetSection(ParsingOptions.SectionName))
            .Validate(o => o.ReviewThreshold is >= 0.5 and <= 0.99, "Parsing:ReviewThreshold must be between 0.5 and 0.99.")
            .ValidateOnStart();

        // Scoped: it depends on the LLM classifier's typed HttpClient, which must not be
        // captured by a singleton.
        services.AddScoped<IBusinessCardIntelligenceService, HybridBusinessCardIntelligenceService>();
        services.AddScoped<Contacts.ContactsService>();
        services.AddSingleton<Observability.BusinessCardMetrics>();
        return services;
    }

    private sealed class ApplicationAssemblyMarker;
}
