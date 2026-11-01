using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Prdb.Api.Controllers;

/// <summary>
/// Checks a key and base URL against <c>GET /rate-limit</c>, which every valid key may call and which
/// changes nothing.
/// </summary>
/// <param name="client">The prdb client.</param>
public sealed class ConnectionTester(IPrdbClient client)
{
    /// <summary>Tests the given values.</summary>
    /// <param name="request">The key and base URL.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The outcome; never throws for a prdb failure.</returns>
    public async Task<ConnectionTestResult> TestAsync(ConnectionTestRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ApiKey))
        {
            return new ConnectionTestResult("missingKey", "Enter the API key of your prdb account.", null);
        }

        var connection = PrdbConnection.From(request.BaseUrl, request.ApiKey);
        if (connection is null)
        {
            return new ConnectionTestResult("invalidUrl", "The base URL must be an absolute http or https URL.", null);
        }

        try
        {
            var status = await client.GetRateLimitAsync(connection, cancellationToken).ConfigureAwait(false);
            return new ConnectionTestResult(
                "ok",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Connected to {0}. {1} of {2} requests left this hour.",
                    connection.BaseUrl.Host,
                    status.Hourly.Remaining,
                    status.Hourly.Limit),
                status.Hourly.Remaining);
        }
        catch (PrdbApiException ex) when (ex.IsUnauthorized)
        {
            return new ConnectionTestResult("unauthorized", "prdb rejected this API key.", null);
        }
        catch (PrdbApiException ex)
        {
            return new ConnectionTestResult("unreachable", ex.Message, null);
        }
    }
}
