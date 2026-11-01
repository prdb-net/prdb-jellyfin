using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.Prdb.Api;

namespace Jellyfin.Plugin.Prdb.Identification;

/// <summary>
/// What one identification run did, for the log.
/// </summary>
public sealed class IdentificationSummary
{
    /// <summary>Gets the number of files asked about.</summary>
    public int Checked { get; internal set; }

    /// <summary>Gets the number of files skipped because a remembered answer still holds.</summary>
    public int Skipped { get; internal set; }

    /// <summary>Gets the applied matches by the rung that matched.</summary>
    public Dictionary<IdentifyMatchKind, int> Applied { get; } = [];

    /// <summary>Gets the number of matches not applied because their switch is off.</summary>
    public int SwitchedOff { get; internal set; }

    /// <summary>Gets the number of ambiguous answers.</summary>
    public int Ambiguous { get; internal set; }

    /// <summary>Gets the number of files prdb found nothing for.</summary>
    public int NoMatch { get; internal set; }

    /// <summary>Gets the number of files that could not be asked about because prdb failed.</summary>
    public int Failed { get; internal set; }

    /// <summary>Gets the total of applied matches.</summary>
    public int AppliedTotal => Applied.Values.Sum();

    /// <inheritdoc />
    public override string ToString()
    {
        var applied = Applied.Count == 0
            ? "0"
            : AppliedTotal.ToString(CultureInfo.InvariantCulture) + " ("
              + string.Join(", ", Applied.OrderBy(a => a.Key).Select(a => a.Key + " " + a.Value.ToString(CultureInfo.InvariantCulture))) + ")";
        return string.Format(
            CultureInfo.InvariantCulture,
            "checked {0}, applied {1}, switched off {2}, ambiguous {3}, no match {4}, skipped {5}, failed {6}",
            Checked,
            applied,
            SwitchedOff,
            Ambiguous,
            NoMatch,
            Skipped,
            Failed);
    }

    internal void Count(IdentifyResult result, MatchDecision decision)
    {
        Checked++;
        switch (decision)
        {
            case MatchDecision.Apply:
                var kind = result.MatchedBy ?? IdentifyMatchKind.OsHash;
                Applied[kind] = Applied.GetValueOrDefault(kind) + 1;
                break;
            case MatchDecision.SwitchedOff:
                SwitchedOff++;
                break;
            case MatchDecision.Ambiguous:
                Ambiguous++;
                break;
            default:
                NoMatch++;
                break;
        }
    }
}
