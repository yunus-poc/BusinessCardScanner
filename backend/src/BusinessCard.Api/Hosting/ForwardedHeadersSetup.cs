using Microsoft.AspNetCore.HttpOverrides;
using IPNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

namespace BusinessCard.Api.Hosting;

/// <summary>
/// Behind a TLS-terminating proxy (ingress, App Gateway, nginx) the API sees plain HTTP from
/// the proxy. X-Forwarded-Proto/-For restore the original scheme and client IP so HTTPS
/// redirection, HSTS and per-client limits behave correctly.
/// </summary>
/// <remarks>
/// Headers are trusted only from the networks in <c>ForwardedHeaders:KnownNetworks</c> (CIDR).
/// Trusting any sender would let clients spoof their IP and scheme. Without configuration only
/// loopback is trusted (ASP.NET Core's default).
/// </remarks>
internal static class ForwardedHeadersSetup
{
    public const string SectionName = "ForwardedHeaders";

    public static IServiceCollection AddTrustedForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        var networks = configuration.GetSection($"{SectionName}:KnownNetworks").Get<string[]>() ?? [];

        return services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // One proxy hop is expected; more would mean an unexpected topology.
            options.ForwardLimit = 1;
            if (networks.Length == 0)
            {
                return;
            }
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
            foreach (var cidr in networks)
            {
                options.KnownNetworks.Add(IPNetwork.Parse(cidr));
            }
        });
    }
}
