using Jellyfin.Plugin.Prdb.Api;
using Jellyfin.Plugin.Prdb.Configuration;
using Jellyfin.Plugin.Prdb.Identification;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Jellyfin.Plugin.Prdb.Tests.Identification;

public sealed class IdentifierTests : IDisposable
{
    private static readonly PrdbConnection Connection = new(new Uri("https://api.example.test"), "key");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "prdb-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakePrdbClient _client = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly List<string> _hashed = [];
    private readonly List<(IdentificationCandidate Candidate, Guid VideoId)> _applied = [];
    private PrdbDataDirectory _data;

    public IdentifierTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "library"));
        _data = new PrdbDataDirectory(Path.Combine(_root, "data"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Matches_the_switches_allow_are_applied_and_the_rest_remembered()
    {
        var exact = File("exact.mp4");
        var probable = File("probable.mp4");
        var ambiguous = File("ambiguous.mp4");
        var none = File("none.mp4");
        _client.Answer("exact.mp4", IdentifyConfidence.Exact, IdentifyMatchKind.OsHash, Video(1));
        _client.Answer("probable.mp4", IdentifyConfidence.Probable, IdentifyMatchKind.ReleaseName, Video(2));
        _client.Answer("ambiguous.mp4", IdentifyConfidence.Ambiguous, null, null);
        _client.Answer("none.mp4", IdentifyConfidence.None, null, null);
        var configuration = new PluginConfiguration { AcceptNameHeuristicMatches = false };

        var summary = await Run(configuration, exact, probable, ambiguous, none);

        var applied = Assert.Single(_applied);
        Assert.Equal(exact, applied.Candidate);
        Assert.Equal(VideoGuid(1), applied.VideoId);
        Assert.Equal(1, summary.Applied[IdentifyMatchKind.OsHash]);
        Assert.Equal(1, summary.SwitchedOff);
        Assert.Equal(1, summary.Ambiguous);
        Assert.Equal(1, summary.NoMatch);
        Assert.Equal(4, summary.Checked);
        Assert.NotNull(new VideoCache(_data, NullLogger<VideoCache>.Instance).TryGet(VideoGuid(1), _time.GetUtcNow()));
    }

    [Fact]
    public async Task The_request_carries_name_size_and_hash_but_no_folder()
    {
        var candidate = File("Some Site - 2024-01-02 - A Title.mp4", size: 1234);
        _client.Answer("Some Site - 2024-01-02 - A Title.mp4", IdentifyConfidence.None, null, null);

        await Run(new PluginConfiguration(), candidate);

        var sent = Assert.Single(Assert.Single(_client.Requests));
        Assert.Equal("Some Site - 2024-01-02 - A Title.mp4", sent.Filename);
        Assert.Equal(1234, sent.Filesize);
        Assert.Equal("hash-of-" + sent.Filename, sent.OsHash);
    }

    [Fact]
    public async Task A_remembered_answer_is_not_asked_again_until_something_changes()
    {
        var candidate = File("none.mp4");
        _client.Answer("none.mp4", IdentifyConfidence.None, null, null);
        var configuration = new PluginConfiguration();

        await Run(configuration, candidate);
        var second = await Run(configuration, candidate);

        Assert.Single(_client.Requests);
        Assert.Equal(1, second.Skipped);
        Assert.Equal(0, second.Checked);
    }

    [Fact]
    public async Task A_change_of_the_switches_asks_again()
    {
        var candidate = File("probable.mp4");
        _client.Answer("probable.mp4", IdentifyConfidence.Probable, IdentifyMatchKind.SiteDateTitle, Video(3));

        await Run(new PluginConfiguration { AcceptNameHeuristicMatches = false }, candidate);
        await Run(new PluginConfiguration { AcceptNameHeuristicMatches = true }, candidate);

        Assert.Equal(2, _client.Requests.Count);
        Assert.Equal(VideoGuid(3), Assert.Single(_applied).VideoId);
    }

    [Fact]
    public async Task A_changed_file_asks_again_and_is_hashed_again()
    {
        var candidate = File("none.mp4", size: 100);
        _client.Answer("none.mp4", IdentifyConfidence.None, null, null);
        var configuration = new PluginConfiguration();

        await Run(configuration, candidate);
        System.IO.File.WriteAllBytes(candidate.Path, new byte[200]);
        await Run(configuration, candidate);

        Assert.Equal(2, _client.Requests.Count);
        Assert.Equal(2, _hashed.Count);
    }

    [Fact]
    public async Task A_stale_answer_is_asked_again_only_when_stale_answers_are_rechecked()
    {
        var candidate = File("none.mp4");
        _client.Answer("none.mp4", IdentifyConfidence.None, null, null);
        var configuration = new PluginConfiguration();

        await Run(configuration, candidate);
        _time.Advance(IdentificationStore.RecheckAfter + TimeSpan.FromMinutes(1));
        await Run(configuration, recheckStale: false, candidate);
        Assert.Single(_client.Requests);

        await Run(configuration, recheckStale: true, candidate);
        Assert.Equal(2, _client.Requests.Count);
        Assert.Single(_hashed);
    }

    [Fact]
    public async Task Remembered_answers_survive_a_restart()
    {
        var candidate = File("none.mp4");
        _client.Answer("none.mp4", IdentifyConfidence.None, null, null);

        await Run(new PluginConfiguration(), candidate);
        _data = new PrdbDataDirectory(_data.Path);
        var afterRestart = await Run(new PluginConfiguration(), candidate);

        Assert.Equal(1, afterRestart.Skipped);
    }

    [Fact]
    public async Task Files_are_sent_in_batches_of_at_most_two_hundred()
    {
        var candidates = Enumerable.Range(0, 250).Select(i => File(i + ".mp4", size: 10)).ToArray();

        var summary = await Run(new PluginConfiguration(), candidates);

        Assert.Equal([200, 50], _client.Requests.Select(r => r.Count));
        Assert.Equal(250, summary.NoMatch);
    }

    [Fact]
    public async Task A_prdb_failure_stops_the_run_and_remembers_nothing_for_the_failed_files()
    {
        var candidate = File("a.mp4");
        _client.Failure = new PrdbApiException("prdb rejected the API key", System.Net.HttpStatusCode.Unauthorized);

        var summary = await Run(new PluginConfiguration(), candidate);

        Assert.Equal(1, summary.Failed);
        _client.Failure = null;
        _client.Answer("a.mp4", IdentifyConfidence.None, null, null);
        var retry = await Run(new PluginConfiguration(), candidate);
        Assert.Equal(1, retry.Checked);
    }

    [Fact]
    public async Task A_missing_file_is_left_out()
    {
        var summary = await Run(new PluginConfiguration(), new IdentificationCandidate(Guid.NewGuid(), Path.Combine(_root, "library", "gone.mp4")));

        Assert.Empty(_client.Requests);
        Assert.Equal(0, summary.Checked);
    }

    [Fact]
    public async Task A_single_file_returns_the_video_to_apply()
    {
        var candidate = File("strong.mp4");
        _client.Answer("strong.mp4", IdentifyConfidence.Strong, IdentifyMatchKind.Filename, Video(4));

        var video = await Identifier().IdentifyOneAsync(Connection, new PluginConfiguration(), candidate.Path, CancellationToken.None);

        Assert.Equal(VideoGuid(4), video!.Id);
    }

    [Fact]
    public async Task A_single_file_the_switches_forbid_returns_nothing_and_is_remembered()
    {
        var candidate = File("strong.mp4");
        _client.Answer("strong.mp4", IdentifyConfidence.Strong, IdentifyMatchKind.Filename, Video(4));
        var configuration = new PluginConfiguration { AcceptExactFilenameMatches = false };

        Assert.Null(await Identifier().IdentifyOneAsync(Connection, configuration, candidate.Path, CancellationToken.None));
        Assert.Null(await Identifier().IdentifyOneAsync(Connection, configuration, candidate.Path, CancellationToken.None));

        Assert.Single(_client.Requests);
    }

    private static Guid VideoGuid(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:000000000000}");

    private static VideoDetail Video(int n) => new(VideoGuid(n), "Video " + n, null, null, null, null, [], []);

    private IdentificationCandidate File(string name, int size = 16)
    {
        var path = Path.Combine(_root, "library", name);
        System.IO.File.WriteAllBytes(path, new byte[size]);
        return new IdentificationCandidate(Guid.NewGuid(), path);
    }

    private Identifier Identifier() => new(
        _client,
        new IdentificationStore(_data, NullLogger<IdentificationStore>.Instance),
        new VideoCache(_data, NullLogger<VideoCache>.Instance),
        NullLogger<Identifier>.Instance,
        path =>
        {
            _hashed.Add(path);
            return "hash-of-" + Path.GetFileName(path);
        },
        _time);

    private Task<IdentificationSummary> Run(PluginConfiguration configuration, params IdentificationCandidate[] candidates) =>
        Run(configuration, recheckStale: true, candidates);

    private Task<IdentificationSummary> Run(PluginConfiguration configuration, bool recheckStale, params IdentificationCandidate[] candidates) =>
        Identifier().IdentifyAsync(
            Connection,
            configuration,
            candidates,
            (candidate, videoId, _) =>
            {
                _applied.Add((candidate, videoId));
                return Task.CompletedTask;
            },
            recheckStale,
            null,
            CancellationToken.None);

    private sealed class FakePrdbClient : IPrdbClient
    {
        private readonly Dictionary<string, (IdentifyConfidence Confidence, IdentifyMatchKind? MatchedBy, VideoDetail? Video)> _answers = [];

        public List<IReadOnlyList<IdentifyFile>> Requests { get; } = [];

        public PrdbApiException? Failure { get; set; }

        public void Answer(string filename, IdentifyConfidence confidence, IdentifyMatchKind? matchedBy, VideoDetail? video) =>
            _answers[filename] = (confidence, matchedBy, video);

        public Task<IReadOnlyList<IdentifyResult>> IdentifyAsync(PrdbConnection connection, IReadOnlyList<IdentifyFile> files, bool includeVideoDetails, CancellationToken cancellationToken)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Requests.Add(files);
            IReadOnlyList<IdentifyResult> results = files.Select(file =>
            {
                var (confidence, matchedBy, video) = _answers.GetValueOrDefault(file.Filename, (IdentifyConfidence.None, null, null));
                return new IdentifyResult(file.Ref, confidence, matchedBy, video?.Id, video, []);
            }).ToList();
            return Task.FromResult(results);
        }

        public Task<VideoDetail?> GetVideoAsync(PrdbConnection connection, Guid videoId, CancellationToken cancellationToken) =>
            Task.FromResult<VideoDetail?>(Video(0) with { Id = videoId });

        public Task<ActorDetail?> GetActorAsync(PrdbConnection connection, Guid actorId, CancellationToken cancellationToken) =>
            Task.FromResult<ActorDetail?>(null);

        public Task<RateLimitStatus> GetRateLimitAsync(PrdbConnection connection, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
