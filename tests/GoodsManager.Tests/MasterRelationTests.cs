using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoodsManager.Web.Masters;
using Microsoft.EntityFrameworkCore;
using static GoodsManager.Tests.AuthenticationTests;

namespace GoodsManager.Tests;

public sealed partial class MasterTests
{
    private async Task<Guid[]> SeedPeers(string kind, int count)
    {
        var ids = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();
        await WithDb(async db =>
        {
            foreach (var id in ids)
            {
                INamedMaster item = kind == "series" ? new Series() : new Work();
                item.Id = id;
                item.Name = item.NormalizedName = "peer";
                item.CreatedAt = item.UpdatedAt = DateTimeOffset.UnixEpoch;
                db.Add(item);
            }
            await db.SaveChangesAsync();
        });
        return ids;
    }

    [TestCase("works", "series")]
    [TestCase("series", "works")]
    [TestCase("characters", "works")]
    public async Task MoreThan100RelationsCanBePagedRenamedAndRemoved(string kind, string peerKind)
    {
        var owner = await Created(kind, new { name = "owner" });
        var peers = await SeedPeers(peerKind, 101);
        Assert.That((await Delta(kind, owner, peers[..100])).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        owner = await Get(kind, Id(owner));
        Assert.That((await Delta(kind, owner, peers[100..])).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        owner = await Get(kind, Id(owner));
        var all = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var response = await client.GetAsync($"/api/v1/masters/{kind}/{Id(owner):D}/relations?page={page}");
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.That(response.Headers.ETag!.ToString(), Is.EqualTo(Etag(owner)));
            Assert.That(result.GetProperty("masterEtag").GetString(), Is.EqualTo(Etag(owner)));
            Assert.That(result.GetProperty("masterRevision").GetString(), Is.EqualTo("3"));
            Assert.That(result.GetProperty("totalCount").GetInt64(), Is.EqualTo(101));
            Assert.That(result.GetProperty("totalPages").GetInt64(), Is.EqualTo(3));
            all.AddRange(result.GetProperty("items").EnumerateArray().Select(Id));
        }
        Assert.That(all, Is.EqualTo(peers.Order()));
        Assert.That((await Related(kind, Id(owner), int.MaxValue, 100)).GetProperty("items").GetArrayLength(), Is.Zero);
        Assert.That(owner.TryGetProperty("seriesIds", out _), Is.False);
        Assert.That(owner.TryGetProperty("workIds", out _), Is.False);
        Assert.That((await Save(kind, new { name = "renamed" }, Id(owner), Etag(owner))).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        owner = await Get(kind, Id(owner));
        Assert.That((await Related(kind, Id(owner))).GetProperty("totalCount").GetInt64(), Is.EqualTo(101));
        Assert.That((await Delta(kind, owner, remove: [peers[0]])).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await Related(kind, Id(owner))).GetProperty("totalCount").GetInt64(), Is.EqualTo(100));
    }

    [Test]
    public async Task Simultaneous100AddsAnd100RemovesAdvanceEachAffectedRevisionOnce()
    {
        var owner = await Created("series", new { name = "s" });
        var peers = await SeedPeers("works", 200);
        await Delta("series", owner, peers[..100]);
        owner = await Get("series", Id(owner));
        var before = await Revision();
        Assert.That((await Delta("series", owner, peers[100..], peers[..100])).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await Revision(), Is.EqualTo(before + 1));
        Assert.That((await Get("series", Id(owner))).GetProperty("revision").GetString(), Is.EqualTo("3"));
        await WithDb(async db =>
        {
            Assert.That(await db.Set<Work>().CountAsync(x => peers.Take(100).Contains(x.Id) && x.Revision == 3), Is.EqualTo(100));
            Assert.That(await db.Set<Work>().CountAsync(x => peers.Skip(100).Contains(x.Id) && x.Revision == 2), Is.EqualTo(100));
            Assert.That(await db.Set<WorkSeries>().Select(x => x.WorkId).ToArrayAsync(), Is.EquivalentTo(peers[100..]));
        });
    }

    [Test]
    public async Task ExistingAddAndAbsentRemoveAreNoOpsAndDoNotTouchEitherSide()
    {
        var owner = await Created("works", new { name = "w" });
        var peers = await SeedPeers("series", 2);
        await Delta("works", owner, [peers[0]]);
        owner = await Get("works", Id(owner));
        var before = await Revision();
        var response = await Delta("works", owner, [peers[0]], [peers[1]]);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Headers.ETag!.ToString(), Is.EqualTo(Etag(owner)));
        Assert.That((await Delta("works", owner)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await Revision(), Is.EqualTo(before));
        Assert.That((await Get("series", peers[0])).GetProperty("revision").GetString(), Is.EqualTo("2"));
        Assert.That((await Get("series", peers[1])).GetProperty("revision").GetString(), Is.EqualTo("1"));
    }

    [TestCase("{\"addIds\":[],\"removeIds\":null}")]
    [TestCase("{\"addIds\":null,\"removeIds\":[]}")]
    [TestCase("{\"addIds\":[],\"removeIds\":[],\"extra\":1}")]
    [TestCase("{\"addIds\":[],\"addIds\":[]}")]
    [TestCase("{\"addIds\":[]}")]
    [TestCase("{\"addIds\":[42],\"removeIds\":[]}")]
    [TestCase("{\"addIds\":[\"\\uD800\"],\"removeIds\":[]}")]
    public async Task InvalidDeltaShapesReturn400WithoutChanges(string raw)
    {
        var owner = await Created("works", new { name = "w" });
        var before = await Revision();
        Assert.That((await Save("works", raw, Id(owner), Etag(owner), relations: true)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await Revision(), Is.EqualTo(before));
    }

    [Test]
    public async Task RemoveLimitsOverlapAndUnknownIdsRejectTheWholeDelta()
    {
        var owner = await Created("series", new { name = "s" });
        var peers = await SeedPeers("works", 2);
        await Delta("series", owner, [peers[0]]);
        owner = await Get("series", Id(owner));
        var before = await Revision();
        foreach (var (add, remove) in new[]
        {
            (Array.Empty<Guid>(), Enumerable.Range(0,101).Select(_ => Guid.NewGuid()).ToArray()),
            (Array.Empty<Guid>(), new[] { peers[0], peers[0] }),
            (new[] { peers[1] }, new[] { peers[1] }),
            (new[] { peers[1] }, new[] { Guid.NewGuid() }),
            (new[] { Guid.NewGuid() }, new[] { peers[0] })
        }) Assert.That((await Delta("series", owner, add, remove)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await Revision(), Is.EqualTo(before));
        Assert.That((await Related("series", Id(owner))).GetProperty("totalCount").GetInt64(), Is.EqualTo(1));
        Assert.That((await Get("works", peers[1])).GetProperty("revision").GetString(), Is.EqualTo("1"));
    }

    [Test]
    public async Task RelationRoutesEnforceAuthenticationCsrfKeysVersionsAndSize()
    {
        var owner = await Created("works", new { name = "w" });
        var delta = new { addIds = Array.Empty<Guid>(), removeIds = Array.Empty<Guid>() };
        using var anonymous = Client(app);
        var path = $"/api/v1/masters/works/{Id(owner):D}/relations";
        Assert.That((await anonymous.GetAsync(path)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await Delta("works", owner, sender: anonymous)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await client.PostAsJsonAsync(path, delta)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await Save("works", delta, Id(owner), Etag(owner), includeKey: false, relations: true)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((int)(await Save("works", delta, Id(owner), relations: true)).StatusCode, Is.EqualTo(428));
        Assert.That((await Save("works", delta, Id(owner), "W/" + Etag(owner), relations: true)).StatusCode, Is.EqualTo(HttpStatusCode.PreconditionFailed));
        Assert.That((await Save("works", new string(' ', 65536) + "{}", Id(owner), Etag(owner), chunked: true, relations: true)).StatusCode, Is.EqualTo(HttpStatusCode.RequestEntityTooLarge));
        Assert.That((await client.GetAsync(path + "?page=0")).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var tag = await Created("tags", new { name = "t" });
        Assert.That((await Delta("tags", tag)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.GetAsync($"/api/v1/masters/tags/{Id(tag):D}/relations")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await Save("works", new { name = "changed", seriesIds = Array.Empty<Guid>() }, Id(owner), Etag(owner))).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await Save("characters", new { name = "c", workIds = Array.Empty<Guid>() })).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task SuccessfulDeltaReplayPrecedesStaleVersionAndDoesNotReapplyChanges()
    {
        var owner = await Created("series", new { name = "s" });
        var peers = await SeedPeers("works", 2);
        var key = Guid.NewGuid();
        var original = await Delta("series", owner, [peers[0]], key: key);
        var updated = await original.Content.ReadFromJsonAsync<JsonElement>();
        await Delta("series", updated, [peers[1]], [peers[0]]);
        var before = await Revision();
        var replay = await Delta("series", owner, [peers[0]], key: key);
        Assert.That(replay.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(replay.Headers.ETag, Is.EqualTo(original.Headers.ETag));
        Assert.That(await replay.Content.ReadAsStringAsync(), Is.EqualTo(await original.Content.ReadAsStringAsync()));
        Assert.That(await Revision(), Is.EqualTo(before));
        var list = await Related("series", Id(owner));
        Assert.That(list.GetProperty("items").EnumerateArray().Select(Id), Is.EqualTo(new[] { peers[1] }));
        Assert.That((await Delta("series", owner, [peers[1]], key: key)).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That((await Delta("series", updated, [peers[0]], key: key)).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task ConcurrentOppositeSideEditsHaveOneWinnerAndOnlyOneSuccessRecord()
    {
        var work = await Created("works", new { name = "w" });
        var series = await Created("series", new { name = "s" });
        using var second = Client(app);
        await SendAsync(second, "auth/password", new { loginId = "owner", password = Password, rememberMe = false });
        var before = await Revision();
        var responses = await Task.WhenAll(Delta("works", work, [Id(series)]), Delta("series", series, [Id(work)], sender: second));
        Assert.That(responses.Select(x => x.StatusCode), Is.EquivalentTo(new[] { HttpStatusCode.OK, HttpStatusCode.PreconditionFailed }));
        Assert.That(await Revision(), Is.EqualTo(before + 1));
        await WithDb(async db => Assert.That(await db.Set<IdempotencyRecord>().CountAsync(x => x.OperationScope.EndsWith("/relations")), Is.EqualTo(1)));
    }

    [Test]
    public async Task ConcurrentSameKeyDeltaIsAppliedOnce()
    {
        var owner = await Created("works", new { name = "w" });
        var peers = await SeedPeers("series", 1);
        using var second = Client(app);
        await SendAsync(second, "auth/password", new { loginId = "owner", password = Password, rememberMe = false });
        var key = Guid.NewGuid();
        var before = await Revision();
        var responses = await Task.WhenAll(Delta("works", owner, peers, key: key), Delta("works", owner, peers, key: key, sender: second));
        Assert.That(responses.Select(x => x.StatusCode), Is.All.EqualTo(HttpStatusCode.OK));
        Assert.That(await responses[0].Content.ReadAsStringAsync(), Is.EqualTo(await responses[1].Content.ReadAsStringAsync()));
        Assert.That(await Revision(), Is.EqualTo(before + 1));
    }

    [Test]
    public async Task MixedDeltaFailureRestoresRemovedEdgesAndBothPeerVersions()
    {
        var owner = await Created("series", new { name = "s" });
        var peers = await SeedPeers("works", 2);
        await Delta("series", owner, [peers[0]]);
        owner = await Get("series", Id(owner));
        var before = await Revision();
        await WithDb(db => db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_delta BEFORE INSERT ON IdempotencyRecords BEGIN SELECT RAISE(ABORT,'test failure'); END"));
        var failedKey = Guid.NewGuid();
        Assert.That((await Delta("series", owner, [peers[1]], [peers[0]], failedKey)).StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That(await Revision(), Is.EqualTo(before));
        Assert.That(Etag(await Get("series", Id(owner))), Is.EqualTo(Etag(owner)));
        Assert.That((await Related("series", Id(owner))).GetProperty("items").EnumerateArray().Select(Id), Is.EqualTo(new[] { peers[0] }));
        Assert.That((await Get("works", peers[0])).GetProperty("revision").GetString(), Is.EqualTo("2"));
        Assert.That((await Get("works", peers[1])).GetProperty("revision").GetString(), Is.EqualTo("1"));
        await WithDb(async db => Assert.That(await db.Set<IdempotencyRecord>().AnyAsync(x => x.Key == failedKey), Is.False));
    }
}
