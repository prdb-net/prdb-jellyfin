using Jellyfin.Plugin.Prdb.Api;
using Jellyfin.Plugin.Prdb.Configuration;
using Jellyfin.Plugin.Prdb.Identification;

namespace Jellyfin.Plugin.Prdb.Tests.Identification;

public class MatchPolicyTests
{
    private static readonly Guid VideoId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static TheoryData<IdentifyConfidence, bool, bool, bool, MatchDecision> Cases()
    {
        var data = new TheoryData<IdentifyConfidence, bool, bool, bool, MatchDecision>();
        foreach (var hash in new[] { true, false })
        {
            foreach (var filename in new[] { true, false })
            {
                foreach (var heuristic in new[] { true, false })
                {
                    data.Add(IdentifyConfidence.Exact, hash, filename, heuristic, hash ? MatchDecision.Apply : MatchDecision.SwitchedOff);
                    data.Add(IdentifyConfidence.Strong, hash, filename, heuristic, filename ? MatchDecision.Apply : MatchDecision.SwitchedOff);
                    data.Add(IdentifyConfidence.Probable, hash, filename, heuristic, heuristic ? MatchDecision.Apply : MatchDecision.SwitchedOff);
                    data.Add(IdentifyConfidence.Ambiguous, hash, filename, heuristic, MatchDecision.Ambiguous);
                    data.Add(IdentifyConfidence.Partial, hash, filename, heuristic, MatchDecision.NoMatch);
                    data.Add(IdentifyConfidence.None, hash, filename, heuristic, MatchDecision.NoMatch);
                    data.Add((IdentifyConfidence)42, hash, filename, heuristic, MatchDecision.NoMatch);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Each_confidence_is_governed_by_its_own_switch(IdentifyConfidence confidence, bool hash, bool filename, bool heuristic, MatchDecision expected)
    {
        var configuration = new PluginConfiguration
        {
            AcceptHashMatches = hash,
            AcceptExactFilenameMatches = filename,
            AcceptNameHeuristicMatches = heuristic,
        };
        var result = new IdentifyResult("a", confidence, IdentifyMatchKind.Filename, VideoId, null, []);

        Assert.Equal(expected, MatchPolicy.Decide(result, configuration));
    }

    [Fact]
    public void A_confident_result_without_a_video_is_never_applied()
    {
        var result = new IdentifyResult("a", IdentifyConfidence.Exact, IdentifyMatchKind.OsHash, null, null, []);

        Assert.Equal(MatchDecision.NoMatch, MatchPolicy.Decide(result, new PluginConfiguration()));
    }

    [Fact]
    public void The_switches_key_changes_with_every_switch()
    {
        var keys = new HashSet<string>();
        foreach (var hash in new[] { true, false })
        {
            foreach (var filename in new[] { true, false })
            {
                foreach (var heuristic in new[] { true, false })
                {
                    keys.Add(MatchPolicy.SwitchesKey(new PluginConfiguration
                    {
                        AcceptHashMatches = hash,
                        AcceptExactFilenameMatches = filename,
                        AcceptNameHeuristicMatches = heuristic,
                    }));
                }
            }
        }

        Assert.Equal(8, keys.Count);
    }
}
