using Jellyfin.Plugin.Prdb.Collections;

namespace Jellyfin.Plugin.Prdb.Tests.Collections;

public class StudioCollectionPlanTests
{
    private static readonly Guid A = Id(1);
    private static readonly Guid B = Id(2);
    private static readonly Guid C = Id(3);

    [Fact]
    public void Switched_on_it_creates_one_collection_per_site_and_network_by_name()
    {
        var plan = StudioCollectionPlan.For(
            enabled: true,
            [Member(A, "Site B", "Network"), Member(B, "Site A", "Network"), Member(C, "Site B")],
            []);

        Assert.Equal(["Network", "Site A", "Site B"], plan.Create.Select(c => c.Studio));
        Assert.Equal([A, B], plan.Create[0].Members);
        Assert.Equal([B], plan.Create[1].Members);
        Assert.Equal([A, C], plan.Create[2].Members);
        Assert.Empty(plan.Change);
        Assert.Empty(plan.Delete);
    }

    [Fact]
    public void Studios_match_without_regard_to_case_or_blanks_and_empty_ones_are_ignored()
    {
        var plan = StudioCollectionPlan.For(
            enabled: true,
            [Member(A, " Site A "), Member(B, "site a", "", "  "), Member(B, "SITE A")],
            []);

        var created = Assert.Single(plan.Create);
        Assert.Equal("Site A", created.Studio);
        Assert.Equal([A, B], created.Members);
    }

    [Fact]
    public void An_existing_collection_gets_what_is_missing_and_loses_what_no_longer_belongs()
    {
        var collection = Id(100);

        var plan = StudioCollectionPlan.For(
            enabled: true,
            [Member(A, "Site A"), Member(B, "Site A"), Member(C, "Site B")],
            [new StudioCollection(collection, "site a", [B, C])]);

        var change = Assert.Single(plan.Change);
        Assert.Equal(collection, change.CollectionId);
        Assert.Equal([A], change.Add);
        Assert.Equal([C], change.Remove);
        Assert.Equal("Site B", Assert.Single(plan.Create).Studio);
        Assert.Empty(plan.Delete);
    }

    [Fact]
    public void A_collection_that_matches_already_needs_nothing()
    {
        var plan = StudioCollectionPlan.For(
            enabled: true,
            [Member(A, "Site A"), Member(B, "Site A")],
            [new StudioCollection(Id(100), "Site A", [B, A])]);

        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void A_collection_whose_studio_has_no_video_left_is_deleted_rather_than_left_empty()
    {
        var gone = Id(100);

        var plan = StudioCollectionPlan.For(
            enabled: true,
            [Member(A, "Site A")],
            [new StudioCollection(Id(101), "Site A", [A]), new StudioCollection(gone, "Site B", [B])]);

        Assert.Equal([gone], plan.Delete);
        Assert.Empty(plan.Change);
        Assert.Empty(plan.Create);
    }

    [Fact]
    public void A_second_collection_for_the_same_studio_is_deleted()
    {
        var first = Id(100);
        var second = Id(101);

        var plan = StudioCollectionPlan.For(
            enabled: true,
            [Member(A, "Site A")],
            [new StudioCollection(first, "Site A", [A]), new StudioCollection(second, "SITE A", [A])]);

        Assert.Equal([second], plan.Delete);
        Assert.Empty(plan.Create);
    }

    [Fact]
    public void Switched_off_it_deletes_every_collection_it_made_and_creates_none()
    {
        var plan = StudioCollectionPlan.For(
            enabled: false,
            [Member(A, "Site A"), Member(B, "Site B")],
            [new StudioCollection(Id(100), "Site A", [A]), new StudioCollection(Id(101), "Site B", [B])]);

        Assert.Equal([Id(100), Id(101)], plan.Delete);
        Assert.Empty(plan.Create);
        Assert.Empty(plan.Change);
    }

    [Fact]
    public void Videos_without_a_studio_create_nothing()
    {
        var plan = StudioCollectionPlan.For(enabled: true, [Member(A)], []);

        Assert.True(plan.IsEmpty);
    }

    private static StudioMember Member(Guid id, params string[] studios) => new(id, studios);

    private static Guid Id(int n) => new($"00000000-0000-0000-0000-{n:000000000000}");
}
