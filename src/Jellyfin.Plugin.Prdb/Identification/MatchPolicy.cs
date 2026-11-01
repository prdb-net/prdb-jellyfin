using Jellyfin.Plugin.Prdb.Api;
using Jellyfin.Plugin.Prdb.Configuration;

namespace Jellyfin.Plugin.Prdb.Identification;

/// <summary>
/// Decides which identification results are applied automatically. The administrator's switches go by
/// prdb's confidence, not by the rung that matched: the confidence is prdb's statement of how far a match
/// can be trusted, and the rung is reported for the log only.
/// </summary>
public static class MatchPolicy
{
    /// <summary>Decides what to do with a result.</summary>
    /// <param name="result">The identification result.</param>
    /// <param name="configuration">The administrator's switches.</param>
    /// <returns>The decision.</returns>
    public static MatchDecision Decide(IdentifyResult result, PluginConfiguration configuration)
    {
        System.ArgumentNullException.ThrowIfNull(result);
        System.ArgumentNullException.ThrowIfNull(configuration);

        if (result.Confidence == IdentifyConfidence.Ambiguous)
        {
            return MatchDecision.Ambiguous;
        }

        var switchedOn = result.Confidence switch
        {
            IdentifyConfidence.Exact => configuration.AcceptHashMatches,
            IdentifyConfidence.Strong => configuration.AcceptExactFilenameMatches,
            IdentifyConfidence.Probable => configuration.AcceptNameHeuristicMatches,

            // None, Partial and anything a newer API might add carry no video the plugin may apply.
            _ => (bool?)null,
        };

        if (switchedOn is null || result.VideoId is null)
        {
            return MatchDecision.NoMatch;
        }

        return switchedOn.Value ? MatchDecision.Apply : MatchDecision.SwitchedOff;
    }

    /// <summary>
    /// A short fingerprint of the switches, stored with each remembered result so that a change of the
    /// switches makes the plugin ask again.
    /// </summary>
    /// <param name="configuration">The administrator's switches.</param>
    /// <returns>Three characters, one per switch.</returns>
    public static string SwitchesKey(PluginConfiguration configuration)
    {
        System.ArgumentNullException.ThrowIfNull(configuration);
        return string.Concat(
            configuration.AcceptHashMatches ? "h" : "-",
            configuration.AcceptExactFilenameMatches ? "f" : "-",
            configuration.AcceptNameHeuristicMatches ? "n" : "-");
    }
}
