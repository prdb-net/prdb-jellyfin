using Jellyfin.Plugin.Prdb.Api;
using Jellyfin.Plugin.Prdb.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Prdb.Tests.Providers;

public class ImageAndPersonProviderTests
{
    private static readonly Guid ActorId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void A_video_offers_one_primary_image_and_its_other_images_as_backdrops()
    {
        var images = VideoImages.Ranked([
            new VideoImage(Guid.NewGuid(), "https://cdn.example/cover.jpg"),
            new VideoImage(Guid.NewGuid(), "https://cdn.example/a.jpg"),
            new VideoImage(Guid.NewGuid(), "https://cdn.example/b.jpg"),
        ]);

        var remote = PrdbMovieImageProvider.ToRemoteImages(images, "prdb").ToList();

        Assert.Equal([ImageType.Primary, ImageType.Backdrop, ImageType.Backdrop], remote.Select(i => i.Type));
        Assert.Equal("https://cdn.example/cover.jpg", remote[0].Url);
        Assert.All(remote, image => Assert.Equal("prdb", image.ProviderName));
    }

    [Fact]
    public void A_video_without_images_offers_none()
    {
        Assert.Empty(PrdbMovieImageProvider.ToRemoteImages(VideoImages.Ranked([]), "prdb"));
        Assert.Empty(PrdbMovieImageProvider.ToRemoteImages(VideoImages.Ranked(null), "prdb"));
    }

    [Fact]
    public void An_actor_fills_the_person()
    {
        var actor = new ActorDetail(
            ActorId,
            "Jane Doe",
            new DateOnly(1990, 1, 2),
            null,
            " Somewhere ",
            "American",
            170,
            [],
            [new ActorBio(Guid.NewGuid(), "  "), new ActorBio(Guid.NewGuid(), " A biography. ")]);

        var result = PrdbPersonProvider.Map(actor);

        Assert.True(result.HasMetadata);
        Assert.Equal("Jane Doe", result.Item.Name);
        Assert.Equal("A biography.", result.Item.Overview);
        Assert.Equal(new DateTime(1990, 1, 2, 0, 0, 0, DateTimeKind.Utc), result.Item.PremiereDate);
        Assert.Null(result.Item.EndDate);
        Assert.Equal(["Somewhere"], result.Item.ProductionLocations);
        Assert.Equal(ActorId.ToString(), result.Item.GetProviderId(PrdbIds.ProviderKey));
    }

    [Theory]
    [InlineData(null, "American", "American")]
    [InlineData(null, "Unknown", null)]
    [InlineData(null, null, null)]
    public void Without_a_birthplace_the_nationality_stands_in_unless_it_is_unknown(string? birthplace, string? nationality, string? expected)
    {
        var actor = new ActorDetail(ActorId, "Jane Doe", null, new DateOnly(2020, 1, 1), birthplace, nationality, null, null, null);

        var result = PrdbPersonProvider.Map(actor);

        if (expected is null)
        {
            Assert.True(result.Item.ProductionLocations is null || result.Item.ProductionLocations.Length == 0);
        }
        else
        {
            Assert.Equal([expected], result.Item.ProductionLocations);
        }

        Assert.Equal(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), result.Item.EndDate);
        Assert.Null(result.Item.Overview);
    }
}
