using Jellyfin.Plugin.Prdb.Collections;
using Jellyfin.Plugin.Prdb.ExternalIds;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Prdb.Tests.Collections;

public class StudioCollectionMarkTests
{
    [Fact]
    public void Only_a_collection_carrying_the_studio_mark_is_the_plugins()
    {
        var marked = new BoxSet();
        marked.SetProviderId(StudioCollections.ProviderKey, "Site A");
        var foreign = new BoxSet();
        foreign.SetProviderId(MetadataProvider.TmdbCollection, "123");
        var movie = new Movie();
        movie.SetProviderId(StudioCollections.ProviderKey, "Site A");

        Assert.True(StudioCollections.IsStudioCollection(marked));
        Assert.False(StudioCollections.IsStudioCollection(foreign));
        Assert.False(StudioCollections.IsStudioCollection(movie));
        Assert.True(new StudioCollectionImageProvider().Supports(marked));
        Assert.False(new StudioCollectionImageProvider().Supports(foreign));
    }

    [Fact]
    public void The_mark_is_a_known_provider_id_of_collections_so_jellyfin_keeps_it()
    {
        var externalId = new PrdbStudioExternalId();

        Assert.Equal(StudioCollections.ProviderKey, externalId.Key);
        Assert.True(externalId.Supports(new BoxSet()));
        Assert.False(externalId.Supports(new Movie()));
    }

    [Fact]
    public void A_cover_counts_only_when_its_file_is_on_disk()
    {
        var file = Path.GetTempFileName();
        try
        {
            Assert.True(StudioCollectionImageProvider.HasLocalCover(WithCover(file)));
            Assert.False(StudioCollectionImageProvider.HasLocalCover(WithCover(file + ".missing")));
            Assert.False(StudioCollectionImageProvider.HasLocalCover(WithCover("https://cdn.example/cover.jpg")));
            Assert.False(StudioCollectionImageProvider.HasLocalCover(new Movie()));
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static Movie WithCover(string path) =>
        new() { ImageInfos = [new ItemImageInfo { Path = path, Type = ImageType.Primary }] };
}
