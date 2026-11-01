using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Prdb.Api;
using Jellyfin.Plugin.Prdb.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Prdb.Providers;

/// <summary>
/// Fills in performers that carry a prdb actor id, which the movie provider gives every performer it adds.
/// </summary>
public class PrdbPersonProvider : IRemoteMetadataProvider<Person, PersonLookupInfo>, IHasOrder
{
    private readonly IPrdbClient _client;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PrdbPersonProvider> _logger;

    /// <summary>Initializes a new instance of the <see cref="PrdbPersonProvider"/> class.</summary>
    /// <param name="client">The prdb client.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public PrdbPersonProvider(IPrdbClient client, IHttpClientFactory httpClientFactory, ILogger<PrdbPersonProvider> logger)
    {
        _client = client;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => PrdbIds.ProviderName;

    /// <inheritdoc />
    public int Order => -1;

    /// <inheritdoc />
    public async Task<MetadataResult<Person>> GetMetadata(PersonLookupInfo info, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(info);

        var actor = await GetActorAsync(info, cancellationToken).ConfigureAwait(false);
        return actor is null ? new MetadataResult<Person>() : Map(actor);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(PersonLookupInfo searchInfo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(searchInfo);

        var actor = await GetActorAsync(searchInfo, cancellationToken).ConfigureAwait(false);
        if (actor is null)
        {
            return [];
        }

        var result = new RemoteSearchResult
        {
            Name = actor.Name,
            SearchProviderName = Name,
            ImageUrl = ActorImages.Best(actor.Images)?.Url,
        };
        result.ProviderIds[PrdbIds.ProviderKey] = actor.Id.ToString("D", CultureInfo.InvariantCulture);
        return [result];
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken) =>
        _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(new Uri(url), cancellationToken);

    /// <summary>Maps a prdb actor onto a Jellyfin person.</summary>
    /// <param name="actor">The actor.</param>
    /// <returns>The result.</returns>
    internal static MetadataResult<Person> Map(ActorDetail actor)
    {
        var person = new Person
        {
            Name = actor.Name,
            Overview = actor.Bios?.Select(bio => bio.Text?.Trim()).FirstOrDefault(text => !string.IsNullOrEmpty(text)),
            PremiereDate = actor.Birthday?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            EndDate = actor.Deathday?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        };

        var place = !string.IsNullOrWhiteSpace(actor.Birthplace) ? actor.Birthplace.Trim()
            : !string.IsNullOrWhiteSpace(actor.NationalityLabel) && !string.Equals(actor.NationalityLabel, "Unknown", StringComparison.OrdinalIgnoreCase) ? actor.NationalityLabel.Trim()
            : null;
        if (place is not null)
        {
            person.ProductionLocations = [place];
        }

        person.SetProviderId(PrdbIds.ProviderKey, actor.Id.ToString("D", CultureInfo.InvariantCulture));
        return new MetadataResult<Person> { Item = person, HasMetadata = true };
    }

    private async Task<ActorDetail?> GetActorAsync(PersonLookupInfo info, CancellationToken cancellationToken)
    {
        var connection = PrdbSettings.TryGetConnection(PrdbSettings.Current, _logger);
        if (connection is null || !PrdbIdParser.TryGet(info, out var actorId))
        {
            return null;
        }

        try
        {
            return await _client.GetActorAsync(connection, actorId, cancellationToken).ConfigureAwait(false);
        }
        catch (PrdbApiException ex)
        {
            _logger.LogWarning("prdb actor {ActorId} is unavailable: {Reason}", actorId, ex.Message);
            return null;
        }
    }
}
