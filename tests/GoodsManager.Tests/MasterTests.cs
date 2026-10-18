using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static GoodsManager.Tests.AuthenticationTests;

namespace GoodsManager.Tests;

/// <summary>実SQLite・HTTPでマスターの境界、競合、成功再送の原子性を検証</summary>
public sealed partial class MasterTests
{
    private TestApplication app = null!;
    private HttpClient client = null!;

    [SetUp]
    public async Task Start()
    {
        app = new TestApplication();
        client = Client(app);
        Assert.That((await SetupAsync(client)).StatusCode, Is.EqualTo(HttpStatusCode.Created));
    }

    [TearDown]
    public async Task Stop() { client.Dispose(); await app.DisposeAsync(); }

    private async Task<HttpResponseMessage> Save(string kind, object value, Guid? id = null, string? etag = null, Guid? key = null, HttpClient? sender = null, bool includeKey = true, bool chunked = false, bool relations = false)
    {
        sender ??= client;
        var csrf = await sender.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(id.HasValue && !relations ? HttpMethod.Put : HttpMethod.Post, $"/api/v1/masters/{kind}" + (id.HasValue ? $"/{id:D}" : "") + (relations ? "/relations" : ""));
        request.Headers.Add("X-CSRF-Token", csrf.GetProperty("token").GetString());
        if (includeKey) request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString("D"));
        if (etag is not null) request.Headers.TryAddWithoutValidation("If-Match", etag);
        request.Content = value is string raw ? chunked ? new ChunkedJson(raw) : new StringContent(raw, Encoding.UTF8, "application/json") : JsonContent.Create(value);
        return await sender.SendAsync(request);
    }

    private async Task<JsonElement> Created(string kind, object value)
    {
        var response = await Save(kind, value);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync());
        Assert.That(response.Headers.Location, Is.Not.Null);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(result.GetProperty("etag").GetString(), Is.EqualTo(response.Headers.ETag!.ToString()));
        return result;
    }

    private async Task WithDb(Func<AppDbContext, Task> action)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task<long> Revision()
    {
        long revision = 0;
        await WithDb(async db => revision = (await db.SystemStates.AsNoTracking().SingleAsync()).DataRevision);
        return revision;
    }

    private static Guid Id(JsonElement value) => value.GetProperty("id").GetGuid();
    private static string Etag(JsonElement value) => value.GetProperty("etag").GetString()!;
    private async Task<JsonElement> Get(string kind, Guid id) => await client.GetFromJsonAsync<JsonElement>($"/api/v1/masters/{kind}/{id:D}");

    private Task<HttpResponseMessage> Delta(string kind, JsonElement item, Guid[]? add = null, Guid[]? remove = null, Guid? key = null, HttpClient? sender = null) =>
        Save(kind, new { addIds = add ?? [], removeIds = remove ?? [] }, Id(item), Etag(item), key, sender, relations: true);

    private Task<JsonElement> Related(string kind, Guid id, int page = 1, int pageSize = 50) =>
        client.GetFromJsonAsync<JsonElement>($"/api/v1/masters/{kind}/{id:D}/relations?page={page}&pageSize={pageSize}");

    [Test]
    public async Task MasterEndpointsEnforceAuthenticationCsrfAndKeys()
    {
        using var anonymous = Client(app);
        Assert.That((await anonymous.GetAsync("/api/v1/masters/tags")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await anonymous.GetAsync("/api/v1/categories/tree")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await Save("tags", new { name = "x" }, sender: anonymous)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await client.PostAsJsonAsync("/api/v1/masters/tags", new { name = "x" })).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await Save("tags", new { name = "x" }, includeKey: false)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        await WithDb(async db => Assert.That(await db.Set<Tag>().CountAsync(), Is.Zero));
    }

    [TestCase("tags", "{\"name\":\"\"}")]
    [TestCase("tags", "{\"name\":\"　 \t\"}")]
    [TestCase("tags", "{\"name\":\"a\\u0000b\"}")]
    [TestCase("tags", "{\"name\":\"\\uD800\"}")]
    [TestCase("tags", "{\"name\":\"a\",\"name\":\"b\"}")]
    [TestCase("tags", "{\"name\":\"a\",\"workIds\":[]}")]
    [TestCase("organizations", "{\"name\":\"a\",\"websiteUrl\":\"javascript:alert(1)\"}")]
    [TestCase("categories", "{\"name\":\"a\",\"sortOrder\":1.5}")]
    public async Task InvalidValuesLeaveNoSuccessRecord(string kind, string raw)
    {
        var before = await Revision();
        Assert.That((await Save(kind, raw)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await Revision(), Is.EqualTo(before));
        await WithDb(async db => Assert.That(await db.Set<IdempotencyRecord>().CountAsync(), Is.Zero));
    }

    [Test]
    public async Task NamesCountCodepointsAndRetainExactDisplayWhileSearchingNormalizedText()
    {
        var longName = string.Concat(Enumerable.Repeat("😀", 200));
        await Created("tags", new { name = longName });
        var invalid = await Save("tags", new { name = longName + "😀" });
        Assert.That(invalid.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("name", out _), Is.True);
        var name = "  ＡＢＣ　\tﾃｽﾄ  ";
        var a = await Created("works", new { name });
        var b = await Created("works", new { name });
        Assert.That(Id(a), Is.Not.EqualTo(Id(b)));
        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/masters/works?q=" + Uri.EscapeDataString("ABC テスト"));
        Assert.That(list.GetProperty("items").GetArrayLength(), Is.EqualTo(2));
        Assert.That(list.GetProperty("items")[0].GetProperty("name").GetString(), Is.EqualTo(name));
        Assert.That(list.GetProperty("items").EnumerateArray().Select(Id), Is.Ordered);
        Assert.That(list.GetProperty("dataRevision").ValueKind, Is.EqualTo(JsonValueKind.String));
        await Created("tags", new { name = "literal%_" });
        var literal = await client.GetFromJsonAsync<JsonElement>("/api/v1/masters/tags?q=%25_");
        Assert.That(literal.GetProperty("items").GetArrayLength(), Is.EqualTo(1));
    }

    [Test]
    public async Task OrganizationLimitsAndExplicitNullsRoundTrip()
    {
        var org = await Created("organizations", new { name = "組織", websiteUrl = "https://example.com/", memo = new string('あ', 4000) });
        Assert.That((await Save("organizations", new { name = "a", memo = new string('a', 4001) })).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await Save("organizations", new { name = "a", websiteUrl = "https://example.com/" + new string('a', 2048) })).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await Save("organizations", new { name = "組織" }, Id(org), Etag(org))).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var clear = await Save("organizations", new { name = "組織", websiteUrl = (string?)null, memo = (string?)null }, Id(org), Etag(org));
        Assert.That(clear.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var after = await Get("organizations", Id(org));
        Assert.That(after.GetProperty("websiteUrl").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(after.GetProperty("memo").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public async Task MissingWeakAndStaleVersionsRefuseWritesAndNoOpPreservesRevisions()
    {
        var item = await Created("tags", new { name = "original" });
        var before = await Revision();
        foreach (var (etag, status) in new (string?, int)[] { (null, 428), ("*", 412), ("W/" + Etag(item), 412), ("\"old\"", 412), (Etag(item) + ", " + Etag(item), 412) })
            Assert.That((int)(await Save("tags", new { name = "changed" }, Id(item), etag)).StatusCode, Is.EqualTo(status));
        var unchanged = await Save("tags", new { name = "original" }, Id(item), Etag(item));
        Assert.That(unchanged.Headers.ETag!.ToString(), Is.EqualTo(Etag(item)));
        Assert.That(await Revision(), Is.EqualTo(before));
    }

    [Test]
    public async Task WorkSeriesChangesInvalidateBothSidesAndRejectStaleReverseEditing()
    {
        var series = await Created("series", new { name = "シリーズ" });
        var work = await Created("works", new { name = "作品" });
        var before = await Revision();
        Assert.That((await Delta("works", work, [Id(series)])).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var updatedSeries = await Get("series", Id(series));
        Assert.That(updatedSeries.GetProperty("revision").GetString(), Is.EqualTo("2"));
        Assert.That((await Related("series", Id(series))).GetProperty("items")[0].GetProperty("id").GetGuid(), Is.EqualTo(Id(work)));
        Assert.That(await Revision(), Is.EqualTo(before + 1));
        Assert.That((await Delta("series", series, remove: [Id(work)])).StatusCode, Is.EqualTo(HttpStatusCode.PreconditionFailed));
        var clear = await Delta("series", updatedSeries, remove: [Id(work)]);
        Assert.That(clear.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var after = await Get("works", Id(work));
        Assert.That(after.GetProperty("revision").GetString(), Is.EqualTo("3"));
        Assert.That((await Related("works", Id(work))).GetProperty("totalCount").GetInt64(), Is.Zero);
    }

    [Test]
    public async Task RelatedIdsRejectMissingDuplicatesAndOversizedArraysWithoutPartialWrites()
    {
        var series = await Created("series", new { name = "s" });
        var work = await Created("works", new { name = "w" });
        var before = await Revision();
        foreach (var ids in new[] { new[] { Id(series), Guid.NewGuid() }, new[] { Id(series), Id(series) }, new[] { Guid.Empty }, Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray() })
            Assert.That((await Delta("works", work, ids)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await Revision(), Is.EqualTo(before));
        Assert.That(Etag(await Get("series", Id(series))), Is.EqualTo(Etag(series)));
        await WithDb(async db => Assert.That(await db.Set<WorkSeries>().CountAsync(), Is.Zero));
    }

    [Test]
    public async Task CharacterRelatedWorkIsPriorityAndDoesNotFilterOtherChoices()
    {
        var work = await Created("works", new { name = "w" });
        var other = await Created("characters", new { name = "a unrelated" });
        var preferred = await Created("characters", new { name = "z related" });
        Assert.That((await Delta("characters", preferred, [Id(work)])).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var list = await client.GetFromJsonAsync<JsonElement>($"/api/v1/masters/characters?relatedWorkIds={Id(work):D}");
        Assert.That(list.GetProperty("items").EnumerateArray().Select(Id), Is.EqualTo(new[] { Id(preferred), Id(other) }));
        Assert.That((await client.GetAsync("/api/v1/masters/tags?page=0")).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await client.GetAsync("/api/v1/masters/tags?pageSize=26")).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await client.GetAsync("/api/v1/masters/tags?page=oops")).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task SuccessfulReplayPrecedesVersionCheckAndReturnsExactOriginalResponse()
    {
        var key = Guid.NewGuid();
        var first = await Save("tags", new { name = "first" }, key: key);
        var original = await first.Content.ReadAsStringAsync();
        var item = JsonDocument.Parse(original).RootElement;
        var updateKey = Guid.NewGuid();
        var update = await Save("tags", new { name = "second" }, Id(item), Etag(item), updateKey);
        var savedUpdate = await update.Content.ReadAsStringAsync();
        var second = JsonDocument.Parse(savedUpdate).RootElement;
        await Save("tags", new { name = "third" }, Id(item), Etag(second));
        var replay = await Save("tags", new { name = "second" }, Id(item), Etag(item), updateKey);
        Assert.That(replay.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await replay.Content.ReadAsStringAsync(), Is.EqualTo(savedUpdate));
        Assert.That(replay.Headers.ETag, Is.EqualTo(update.Headers.ETag));
        var createReplay = await Save("tags", new { name = "first" }, key: key);
        Assert.That(createReplay.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(createReplay.Headers.Location, Is.EqualTo(first.Headers.Location));
        Assert.That(await createReplay.Content.ReadAsStringAsync(), Is.EqualTo(original));
        Assert.That((await Save("tags", new { name = "different" }, key: key)).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That((await Save("tags", "{malformed", key: key)).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That((await Save("tags", new { name = "second" }, Id(item), Etag(second), updateKey)).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        await WithDb(async db => Assert.That(await db.Set<Tag>().CountAsync(), Is.EqualTo(1)));
    }

    [Test]
    public async Task ConcurrentSameKeyCommitsOnceAndConcurrentEditsHaveOneWinner()
    {
        using var second = Client(app);
        await SendAsync(second, "auth/password", new { loginId = "owner", password = Password, rememberMe = false });
        var key = Guid.NewGuid();
        var results = await Task.WhenAll(Save("tags", new { name = "a" }, key: key), Save("tags", new { name = "a" }, key: key, sender: second));
        Assert.That(results.Select(x => x.StatusCode), Is.All.EqualTo(HttpStatusCode.Created));
        Assert.That(await results[0].Content.ReadAsStringAsync(), Is.EqualTo(await results[1].Content.ReadAsStringAsync()));
        var item = await results[0].Content.ReadFromJsonAsync<JsonElement>();
        results = await Task.WhenAll(Save("tags", new { name = "b" }, Id(item), Etag(item)), Save("tags", new { name = "c" }, Id(item), Etag(item), sender: second));
        Assert.That(results.Select(x => x.StatusCode), Is.EquivalentTo(new[] { HttpStatusCode.OK, HttpStatusCode.PreconditionFailed }));
        await WithDb(async db => Assert.That(await db.Set<IdempotencyRecord>().CountAsync(), Is.EqualTo(2)));
    }

    [Test]
    public async Task ReplayExpiryIsAbsoluteAndExpiredRecordCanBeReplaced()
    {
        await SendAsync(client, "auth/password", new { loginId = "owner", password = Password, rememberMe = true });
        var key = Guid.NewGuid();
        var original = await Save("tags", new { name = "a" }, key: key);
        app.Clock.Advance(TimeSpan.FromDays(6));
        var replay = await Save("tags", new { name = "a" }, key: key);
        Assert.That(await replay.Content.ReadAsStringAsync(), Is.EqualTo(await original.Content.ReadAsStringAsync()));
        app.Clock.Advance(TimeSpan.FromDays(1));
        var after = await Save("tags", new { name = "a" }, key: key);
        Assert.That(after.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(after.Headers.Location, Is.Not.EqualTo(original.Headers.Location));
        await WithDb(async db => { Assert.That(await db.Set<Tag>().CountAsync(), Is.EqualTo(2)); Assert.That(await db.Set<IdempotencyRecord>().CountAsync(), Is.EqualTo(1)); });
    }

    [Test]
    public async Task FailedSuccessRecordInsertRollsBackEntityRelationsAndAllRevisions()
    {
        var series = await Created("series", new { name = "s" });
        var work = await Created("works", new { name = "w" });
        var before = await Revision();
        await WithDb(db => db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_record BEFORE INSERT ON IdempotencyRecords BEGIN SELECT RAISE(ABORT,'test failure'); END"));
        var key = Guid.NewGuid();
        var failed = await Delta("works", work, [Id(series)], key: key);
        Assert.That(failed.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That(await Revision(), Is.EqualTo(before));
        Assert.That(Etag(await Get("series", Id(series))), Is.EqualTo(Etag(series)));
        await WithDb(async db =>
        {
            Assert.That(Etag(await Get("works", Id(work))), Is.EqualTo(Etag(work)));
            Assert.That(await db.Set<WorkSeries>().CountAsync(), Is.Zero);
            Assert.That(await db.Set<IdempotencyRecord>().AnyAsync(x => x.Key == key), Is.False);
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_record");
        });
        Assert.That((await Delta("works", work, [Id(series)], key: key)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task CyclicCategoryMovesFailIncludingConcurrentOppositeMoves()
    {
        var a = await Created("categories", new { name = "a" });
        var b = await Created("categories", new { name = "b", parentId = Id(a) });
        var c = await Created("categories", new { name = "c", parentId = Id(b) });
        var before = await Revision();
        Assert.That((await Save("categories", new { name = "a", parentId = Id(c), sortOrder = 0 }, Id(a), Etag(a))).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That((await Save("categories", new { name = "a", parentId = Id(a), sortOrder = 0 }, Id(a), Etag(a))).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await Revision(), Is.EqualTo(before));
        var x = await Created("categories", new { name = "x" });
        var y = await Created("categories", new { name = "y" });
        var results = await Task.WhenAll(
            Save("categories", new { name = "x", parentId = Id(y), sortOrder = 0 }, Id(x), Etag(x)),
            Save("categories", new { name = "y", parentId = Id(x), sortOrder = 0 }, Id(y), Etag(y)));
        Assert.That(results.Select(r => r.StatusCode), Is.EquivalentTo(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
    }

    [Test]
    public async Task BodyLimitIncludesWhitespaceAndRejectsMalformedJson()
    {
        Assert.That((await Save("tags", new string(' ', 65536) + "{\"name\":\"a\"}")).StatusCode, Is.EqualTo(HttpStatusCode.RequestEntityTooLarge));
        Assert.That((await Save("tags", new string(' ', 65536) + "{\"name\":\"a\"}", chunked: true)).StatusCode, Is.EqualTo(HttpStatusCode.RequestEntityTooLarge));
        Assert.That((await Save("tags", "{broken")).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    private sealed class ChunkedJson : HttpContent
    {
        private readonly byte[] bytes;
        public ChunkedJson(string raw) { bytes = Encoding.UTF8.GetBytes(raw); Headers.ContentType = new("application/json"); }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    }

    [Test]
    public async Task OverflowRollsBackAllChangesAndAllowsReadOnlySuccessReplay()
    {
        var key = Guid.NewGuid();
        var created = await Save("tags", new { name = "saved" }, key: key);
        var item = await created.Content.ReadFromJsonAsync<JsonElement>();
        await WithDb(db => db.Database.ExecuteSqlRawAsync("UPDATE SystemState SET DataRevision=9223372036854775807"));
        var failure = await Save("tags", new { name = "changed" }, Id(item), Etag(item));
        Assert.That(failure.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That(Etag(await Get("tags", Id(item))), Is.EqualTo(Etag(item)));
        Assert.That(await Revision(), Is.EqualTo(long.MaxValue));
        Assert.That((await Save("tags", new { name = "saved" }, key: key)).StatusCode, Is.EqualTo(HttpStatusCode.Created));
        await WithDb(async db => Assert.That(await db.Set<IdempotencyRecord>().CountAsync(), Is.EqualTo(1)));
    }

    [Test]
    public async Task ExternalWriterLockReturnsRetryableFailureAndDoesNotRecordSuccess()
    {
        var before = await Revision();
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        await using var connection = new SqliteConnection(new DatabaseRuntime(app.DataDirectory).ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/masters/tags");
        var key = Guid.NewGuid();
        request.Headers.Add("X-CSRF-Token", csrf.GetProperty("token").GetString());
        request.Headers.Add("Idempotency-Key", key.ToString("D"));
        request.Content = JsonContent.Create(new { name = "locked" });
        var response = await Task.Run(() => client.SendAsync(request));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(response.Headers.RetryAfter!.Delta, Is.EqualTo(TimeSpan.FromSeconds(1)));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("DATABASE_BUSY"));
        await transaction.RollbackAsync();
        Assert.That(await Revision(), Is.EqualTo(before));
        await WithDb(async db => Assert.That(await db.Set<IdempotencyRecord>().CountAsync(), Is.Zero));
        Assert.That((await Save("tags", new { name = "locked" }, key: key)).StatusCode, Is.EqualTo(HttpStatusCode.Created));
    }

    [Test]
    public async Task PaginationPreservesTiesAcrossPageBoundaries()
    {
        var ids = new List<Guid>();
        for (var i = 0; i < 27; i++) ids.Add(Id(await Created("tags", new { name = "same name" })));
        var first = await client.GetFromJsonAsync<JsonElement>("/api/v1/masters/tags?page=1&pageSize=25");
        var second = await client.GetFromJsonAsync<JsonElement>("/api/v1/masters/tags?page=2&pageSize=25");
        Assert.That(first.GetProperty("totalCount").GetInt64(), Is.EqualTo(27));
        Assert.That(first.GetProperty("totalPages").GetInt64(), Is.EqualTo(2));
        Assert.That(first.GetProperty("items").EnumerateArray().Select(Id).Concat(second.GetProperty("items").EnumerateArray().Select(Id)), Is.EqualTo(ids.Order()));
        var outside = await client.GetFromJsonAsync<JsonElement>("/api/v1/masters/tags?page=2147483647&pageSize=100");
        Assert.That(outside.GetProperty("items").GetArrayLength(), Is.Zero);
        Assert.That((await client.GetAsync("/api/v1/masters/tags?q=" + new string('a', 201))).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task DatabaseConstraintsUseActualColumnNamesAndRejectInvalidStoredValues()
    {
        await Created("tags", new { name = "tag" });
        await WithDb(async db =>
        {
            Assert.That(db.Database.HasPendingModelChanges(), Is.False);
            foreach (var sql in new[] { "UPDATE Tags SET Name='　'", "UPDATE Tags SET Revision=0", "UPDATE Tags SET Revision=1.5", "UPDATE Tags SET TagId='invalid'", "UPDATE Categories SET ParentCategoryId=CategoryId", "UPDATE Categories SET SortOrder=-1", "INSERT INTO WorkSeries(WorkId,SeriesId) VALUES('00000000-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000002')" })
                Assert.ThrowsAsync<SqliteException>(async () => await db.Database.ExecuteSqlRawAsync(sql));
            var categories = await db.Set<Category>().ToListAsync();
            Assert.That(categories, Has.Count.EqualTo(47));
            Assert.That(categories.Count(x => x.ParentCategoryId is null), Is.EqualTo(9));
        });
    }
}
