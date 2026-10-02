namespace BusinessCard.Infrastructure.Graph;

/// <summary>Configuration section "MicrosoftGraph".</summary>
public sealed class GraphOptions
{
    public const string SectionName = "MicrosoftGraph";

    public Uri BaseUrl { get; set; } = new("https://graph.microsoft.com/v1.0/");

    /// <summary>Delegated scopes requested On-Behalf-Of the signed-in user. Least privilege.</summary>
    public string[] Scopes { get; set; } = ["https://graph.microsoft.com/Contacts.ReadWrite"];

    /// <summary>Per-request budget for Graph calls.</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Longest Retry-After honoured inline (one retry). Longer back-offs are returned to
    /// the app as GRAPH_RATE_LIMITED with Retry-After instead of holding the request open.
    /// </summary>
    public int MaxInlineRetryDelaySeconds { get; set; } = 2;
}
