namespace Jellyfin.Plugin.Prdb.Api.Controllers;

/// <summary>
/// The values to test, as they stand on the configuration page, saved or not.
/// </summary>
public class ConnectionTestRequest
{
    /// <summary>Gets or sets the API key to test.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Gets or sets the base URL to test.</summary>
    public string? BaseUrl { get; set; }
}
