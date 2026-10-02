using System.Text.RegularExpressions;

namespace BusinessCard.Application.BusinessCards.Parsing;

/// <param name="Value">Normalized absolute URL, e.g. "https://www.acme.com.tr".</param>
/// <param name="IsSocial">LinkedIn, X, Instagram…: not the company website.</param>
internal sealed record UrlMatch(string Value, bool IsSocial);

internal static partial class UrlExtractor
{
    private static readonly string[] SocialHosts =
    [
        "linkedin.com", "twitter.com", "x.com", "instagram.com", "facebook.com", "youtube.com", "github.com",
    ];

    /// <summary>Run after <see cref="EmailExtractor"/> so email domains are not mistaken for websites.</summary>
    public static (IReadOnlyList<UrlMatch> Urls, string Remaining) Extract(string text)
    {
        var urls = new List<UrlMatch>();
        var remaining = UrlCandidate().Replace(text, match =>
        {
            if (Normalize(match.Value) is { } url)
            {
                urls.Add(url);
                return " ";
            }
            return match.Value;
        });
        return (urls, remaining);
    }

    private static UrlMatch? Normalize(string candidate)
    {
        var trimmed = candidate.TrimEnd('.', ',', ';', ':', ')', ']', '/');
        var withScheme = trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : "https://" + trimmed;

        if (!Uri.TryCreate(withScheme, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !uri.Host.Contains('.', StringComparison.Ordinal)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }

        var host = uri.IdnHost.ToLowerInvariant();
        var path = uri.AbsolutePath == "/" ? string.Empty : uri.AbsolutePath.TrimEnd('/');
        var normalized = $"{uri.Scheme}://{host}{path}{uri.Query}";
        var bareHost = host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
        var isSocial = SocialHosts.Any(s => bareHost == s || bareHost.EndsWith("." + s, StringComparison.Ordinal));
        return new UrlMatch(normalized, isSocial);
    }

    // Explicit scheme or www., or a bare domain with a common TLD. The TLD list
    // keeps "Ltd.Şti." or "No.5" from ever looking like a domain.
    [GeneratedRegex(
        @"(?<![\w@.\-])(?:https?://[^\s,;]+|www\.[^\s,;]+|(?:[a-z0-9](?:[a-z0-9\-]*[a-z0-9])?\.)+(?:com|net|org|info|biz|io|co|ai|app|dev|tech|online|tr|de|uk|eu|fr|it|nl|es|ch|at|be|us|az|ru)(?:\.[a-z]{2})?(?:/[^\s,;]*)?)(?![\w\-])",
        RegexOptions.IgnoreCase)]
    private static partial Regex UrlCandidate();
}
