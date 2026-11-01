namespace Jellyfin.Plugin.Prdb.Identification;

/// <summary>
/// What the plugin does with one identification result.
/// </summary>
public enum MatchDecision
{
    /// <summary>prdb found nothing, or only a site; there is nothing to apply.</summary>
    NoMatch,

    /// <summary>prdb found several videos equally good; the plugin never guesses.</summary>
    Ambiguous,

    /// <summary>prdb found one video, but the administrator switched this kind of match off.</summary>
    SwitchedOff,

    /// <summary>prdb found one video and the plugin applies it.</summary>
    Apply,
}
