using System;

namespace Jellyfin.Plugin.Prdb.Api;

/// <summary>
/// Where and as whom the plugin talks to prdb.
/// </summary>
/// <param name="BaseUrl">The API's base URL, e.g. <c>https://api.prdb.net</c>.</param>
/// <param name="ApiKey">The prdb API key, sent as <c>X-Api-Key</c>.</param>
public sealed record PrdbConnection(Uri BaseUrl, string ApiKey)
{
    /// <summary>The public prdb API.</summary>
    public static readonly Uri DefaultBaseUrl = new("https://api.prdb.net");

    /// <summary>
    /// Builds a connection from configuration values, or returns <c>null</c> when there is no key or the URL
    /// is not an absolute http(s) URL.
    /// </summary>
    /// <param name="baseUrl">The configured base URL; blank means <see cref="DefaultBaseUrl"/>.</param>
    /// <param name="apiKey">The configured API key.</param>
    /// <returns>The connection, or <c>null</c>.</returns>
    public static PrdbConnection? From(string? baseUrl, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var url = DefaultBaseUrl;
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var parsed)
                || (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp))
            {
                return null;
            }

            url = parsed;
        }

        return new PrdbConnection(url, apiKey.Trim());
    }

    /// <summary>Resolves a path against the base URL, keeping any path prefix the base URL has.</summary>
    /// <param name="relativePath">The path without a leading slash, e.g. <c>videos/identify</c>.</param>
    /// <returns>The absolute URL.</returns>
    public Uri Resolve(string relativePath)
    {
        var root = BaseUrl.AbsoluteUri.EndsWith('/') ? BaseUrl : new Uri(BaseUrl.AbsoluteUri + "/");
        return new Uri(root, relativePath);
    }

    /// <summary>Keeps the key out of logs and exception messages that print the record.</summary>
    /// <returns>The base URL only.</returns>
    public override string ToString() => BaseUrl.AbsoluteUri;
}
