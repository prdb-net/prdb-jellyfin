using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Prdb.Api;
using Jellyfin.Plugin.Prdb.ExternalIds;
using Jellyfin.Plugin.Prdb.Providers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Prdb.Tests.Providers;

public class VideoMetadataMapperTests
{
    private static readonly Guid VideoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ActorId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid StashId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public void A_complete_video_fills_the_movie()
    {
        var video = new VideoDetail(
            VideoId,
            "A Title",
            "  A description.  ",
            StashId,
            new DateOnly(2024, 5, 6),
            new VideoSite(Guid.NewGuid(), "Some Site", "https://site.example", new VideoNetwork(Guid.NewGuid(), "Some Network")),
            [new VideoImage(Guid.NewGuid(), "https://cdn.example/cover.jpg")],
            [new VideoActor(ActorId, "Jane Doe", [
                new ActorImage(Guid.NewGuid(), "https://cdn.example/thumb.jpg", ActorImageType.Thumbnail),
                new ActorImage(Guid.NewGuid(), "https://cdn.example/poster.jpg", ActorImageType.Poster),
            ])]);

        var result = VideoMetadataMapper.Map(video);

        Assert.True(result.HasMetadata);
        var movie = result.Item;
        Assert.Equal("A Title", movie.Name);
        Assert.Equal("A description.", movie.Overview);
        Assert.Equal(new DateTime(2024, 5, 6, 0, 0, 0, DateTimeKind.Utc), movie.PremiereDate);
        Assert.Equal(2024, movie.ProductionYear);
        Assert.Equal(["Some Site", "Some Network"], movie.Studios);
        Assert.Equal(VideoId.ToString(), movie.GetProviderId(PrdbIds.ProviderKey));
        Assert.Equal(StashId.ToString(), movie.GetProviderId(VideoMetadataMapper.StashDbProviderKey));

        var person = Assert.Single(result.People);
        Assert.Equal("Jane Doe", person.Name);
        Assert.Equal(PersonKind.Actor, person.Type);
        Assert.Equal(ActorId.ToString(), person.GetProviderId(PrdbIds.ProviderKey));
        Assert.Equal("https://cdn.example/poster.jpg", person.ImageUrl);
    }

    [Fact]
    public void A_sparse_video_leaves_the_unknown_fields_empty()
    {
        var video = new VideoDetail(VideoId, "Only a title", "   ", null, null, null, null, null);

        var result = VideoMetadataMapper.Map(video);

        Assert.Equal("Only a title", result.Item.Name);
        Assert.Null(result.Item.Overview);
        Assert.Null(result.Item.PremiereDate);
        Assert.Null(result.Item.ProductionYear);
        Assert.Empty(result.Item.Studios);
        Assert.False(result.Item.HasProviderId(VideoMetadataMapper.StashDbProviderKey));
        Assert.True(result.People is null || result.People.Count == 0);
    }

    [Fact]
    public void A_network_named_like_its_site_is_not_listed_twice()
    {
        var video = new VideoDetail(VideoId, "T", null, null, null, new VideoSite(Guid.NewGuid(), "Brand", null, new VideoNetwork(Guid.NewGuid(), "brand")), [], []);

        Assert.Equal(["Brand"], VideoMetadataMapper.Studios(video));
    }

    [Theory]
    [InlineData("11111111-1111-1111-1111-111111111111")]
    [InlineData(" 11111111111111111111111111111111 ")]
    [InlineData("https://prdb.net/videos/11111111-1111-1111-1111-111111111111")]
    [InlineData("https://prdb.net/videos/11111111-1111-1111-1111-111111111111/")]
    public void A_prdb_id_is_read_bare_or_from_its_address(string value)
    {
        Assert.True(PrdbIdParser.TryParse(value, out var id));
        Assert.Equal(VideoId, id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-id")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Anything_else_is_not_a_prdb_id(string? value)
    {
        Assert.False(PrdbIdParser.TryParse(value, out _));
    }

    [Fact]
    public void Movies_and_people_link_to_their_prdb_pages()
    {
        var movie = new Movie();
        movie.SetProviderId(PrdbIds.ProviderKey, VideoId.ToString());
        var person = new Person();
        person.SetProviderId(PrdbIds.ProviderKey, ActorId.ToString());
        var provider = new PrdbExternalUrlProvider();

        Assert.Equal(["https://prdb.net/videos/" + VideoId], provider.GetExternalUrls(movie));
        Assert.Equal(["https://prdb.net/actors/" + ActorId], provider.GetExternalUrls(person));
        Assert.Empty(provider.GetExternalUrls(new Movie()));
    }

    [Fact]
    public void The_oldest_video_image_is_the_primary_and_the_rest_are_backdrops()
    {
        var images = VideoImages.Ranked([
            new VideoImage(Guid.NewGuid(), null),
            new VideoImage(Guid.NewGuid(), "https://cdn.example/1.jpg"),
            new VideoImage(Guid.NewGuid(), "https://cdn.example/2.jpg"),
            new VideoImage(Guid.NewGuid(), "https://cdn.example/3.jpg"),
        ]);

        Assert.Equal("https://cdn.example/1.jpg", images.Primary!.Url);
        Assert.Equal(["https://cdn.example/2.jpg", "https://cdn.example/3.jpg"], images.Backdrops.Select(i => i.Url));
    }

    [Fact]
    public void Actor_images_rank_posters_before_faces_before_thumbnails()
    {
        var ranked = ActorImages.Ranked([
            new ActorImage(Guid.NewGuid(), "https://cdn.example/t.jpg", ActorImageType.Thumbnail),
            new ActorImage(Guid.NewGuid(), "https://cdn.example/f.jpg", ActorImageType.Face),
            new ActorImage(Guid.NewGuid(), null, ActorImageType.Poster),
            new ActorImage(Guid.NewGuid(), "https://cdn.example/p.jpg", ActorImageType.Poster),
        ]);

        Assert.Equal(["https://cdn.example/p.jpg", "https://cdn.example/f.jpg", "https://cdn.example/t.jpg"], ranked.Select(i => i.Url));
    }
}
