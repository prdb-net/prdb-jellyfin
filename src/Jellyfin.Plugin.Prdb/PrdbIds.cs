namespace Jellyfin.Plugin.Prdb;

/// <summary>
/// The names under which the plugin appears in Jellyfin.
/// </summary>
public static class PrdbIds
{
    /// <summary>The provider id key prdb video and actor ids are stored under on Jellyfin items.</summary>
    public const string ProviderKey = "Prdb";

    /// <summary>The provider name shown in the library settings, the metadata editor and the external links.</summary>
    public const string ProviderName = "prdb";

    /// <summary>The prdb.net page of a video, with the id as <c>{0}</c>.</summary>
    public const string VideoUrlFormat = "https://prdb.net/videos/{0}";

    /// <summary>The prdb.net page of an actor, with the id as <c>{0}</c>.</summary>
    public const string ActorUrlFormat = "https://prdb.net/actors/{0}";
}
