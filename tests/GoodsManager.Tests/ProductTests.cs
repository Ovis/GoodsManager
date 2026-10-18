using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Products;
using GoodsManager.Web.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static GoodsManager.Tests.AuthenticationTests;

namespace GoodsManager.Tests;

public sealed class ProductTests
{
    private TestApplication app = null!;
    private HttpClient client = null!;
    private Guid category;
    [SetUp]
    public async Task Start()
    {
        app = new TestApplication(); client = Client(app); await SetupAsync(client);
        await Db(async db => category = await db.Set<Category>().Select(x => x.Id).FirstAsync());
    }
    [TearDown]
    public async Task Stop() { client.Dispose(); await app.DisposeAsync(); }
    private async Task Db(Func<AppDbContext, Task> action) { await using var scope = app.Services.CreateAsyncScope(); await action(scope.ServiceProvider.GetRequiredService<AppDbContext>()); }
    private async Task<long> Revision() { long value = 0; await Db(async db => value = (await db.SystemStates.AsNoTracking().SingleAsync()).DataRevision); return value; }
    private object Draft(object[]? identifiers = null, Guid[]? works = null, Guid[]? characters = null, Guid[]? tags = null, Guid? org = null) =>
        new { name = "P05商品", categoryId = category, version = "版A", manufacturerId = org, sellerId = org, memo = "商品メモ", workIds = works ?? [], characterIds = characters ?? [], tagIds = tags ?? [], identifiers = identifiers ?? [], links = new[] { new { url = "https://example.com/a?x=1#原文" } } };
    private static object Fields(string? condition = "unopened", string? memo = "元ロットメモ") => new { condition, storageLocation = "場所A", purchasePlace = "購入先A", memo };
    private object CreateBody(int quantity = 3, object? product = null) => new { product = product ?? Draft(), initialLot = new { quantity, fields = Fields() } };
    private async Task<HttpResponseMessage> Send(string path, object value, string? etag = null, Guid? key = null, HttpClient? sender = null, bool includeKey = true)
    {
        sender ??= client;
        var csrf = await sender.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/" + path);
        request.Headers.Add("X-CSRF-Token", csrf.GetProperty("token").GetString());
        if (includeKey) request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString("D"));
        if (etag is not null) request.Headers.TryAddWithoutValidation("If-Match", etag);
        request.Content = value is string raw ? new StringContent(raw, Encoding.UTF8, "application/json") : JsonContent.Create(value);
        return await sender.SendAsync(request);
    }
    private static Guid Id(JsonElement p) => p.GetProperty("id").GetGuid();
    private static string Etag(JsonElement p) => p.GetProperty("etag").GetString()!;
    private async Task<JsonElement> Product(int quantity = 3, object? draft = null)
    {
        var response = await Send("dev/products", CreateBody(quantity, draft));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync());
        Assert.That(response.Headers.Location, Is.Not.Null);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private Task<HttpResponseMessage> Commands(JsonElement p, object[] commands, Guid? key = null, string? etag = null) =>
        Send($"products/{Id(p):D}/inventory-commands", new { commands }, etag ?? Etag(p), key);
    private Task<JsonElement> Get(Guid id) => client.GetFromJsonAsync<JsonElement>($"/api/v1/products/{id:D}");
    private Task<JsonElement> Inventory(Guid id) => client.GetFromJsonAsync<JsonElement>($"/api/v1/products/{id:D}/inventory");
    private static string LotId(JsonElement p) => p.GetProperty("inventory").GetProperty("lots")[0].GetProperty("id").GetString()!;
    private async Task<JsonElement> Success(HttpResponseMessage response)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(Etag(body), Is.EqualTo(response.Headers.ETag!.ToString()));
        return body;
    }

    [Test]
    public async Task CreationPreservesCodesAndDistinctMasterRelationsWithoutMultiplyingQuantity()
    {
        var now = app.Clock.GetUtcNow();
        var w1 = new Work { Id = Guid.NewGuid(), Name = "w1", NormalizedName = "w1", CreatedAt = now, UpdatedAt = now };
        var w2 = new Work { Id = Guid.NewGuid(), Name = "w2", NormalizedName = "w2", CreatedAt = now, UpdatedAt = now };
        var org = new Organization { Id = Guid.NewGuid(), Name = "org", NormalizedName = "org", CreatedAt = now, UpdatedAt = now };
        await Db(async db => { db.AddRange(w1, w2, org); await db.SaveChangesAsync(); });
        var raw = " ０-３０６-４０６１５-２　";
        var p = await Product(3, Draft([new { scheme = "isbn10", value = raw }, new { scheme = "isbn13", value = "9780306406157" }], [w1.Id, w2.Id], org: org.Id));
        Assert.That(p.GetProperty("totalQuantity").GetInt64(), Is.EqualTo(3));
        Assert.That(p.GetProperty("works").GetArrayLength(), Is.EqualTo(2));
        var codes = p.GetProperty("identifiers").EnumerateArray().ToArray();
        Assert.That(codes.Select(x => x.GetProperty("comparisonValue").GetString()), Is.All.EqualTo("09780306406157"));
        Assert.That(codes.Single(x => x.GetProperty("scheme").GetString() == "isbn10").GetProperty("rawValue").GetString(), Is.EqualTo(raw));
        var sameCodes = await Product(1, Draft([new { scheme = "ean13", value = "9780306406157" }]));
        Assert.That(Id(sameCodes), Is.Not.EqualTo(Id(p)));
        await Db(async db => Assert.That(await db.Set<ProductIdentifier>().CountAsync(), Is.EqualTo(3)));
    }

    [Test]
    public async Task CommandsResolveEarlierRefsAndPreserveTotalsAndInheritance()
    {
        var p = await Product(5);
        var before = await Revision();
        var result = await Success(await Commands(p, [
            new { type = "splitLot", lotRef = LotId(p), quantity = 2, newLotRef = "split", fields = new { storageLocation = "場所B" } },
            new { type = "extractItems", lotRef = LotId(p), itemRefs = new[] { "one", "two" } },
            new { type = "updateItem", itemRef = "one", fields = new { memo = "傷あり", condition = "opened" } },
            new { type = "returnItem", itemRef = "one", newLotRef = "returned", memoAction = "discardConfirmed", photoAction = "moveToProduct" },
            new { type = "updateLot", lotRef = "split", quantity = 4 }
        ]));
        Assert.That(result.GetProperty("totalQuantity").GetInt64(), Is.EqualTo(7));
        Assert.That(result.GetProperty("productRevision").GetString(), Is.EqualTo("2"));
        Assert.That(await Revision(), Is.EqualTo(before + 1));
        var refs = result.GetProperty("createdRefs");
        var lots = result.GetProperty("lots").EnumerateArray().ToArray();
        Assert.That(lots.Single(x => x.GetProperty("id").GetGuid() == refs.GetProperty("returned").GetGuid()).GetProperty("memo").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(lots.Single(x => x.GetProperty("id").GetGuid() == refs.GetProperty("split").GetGuid()).GetProperty("storageLocation").GetString(), Is.EqualTo("場所B"));
        var item = result.GetProperty("items")[0];
        Assert.That(item.GetProperty("memo").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(item.GetProperty("storageLocation").GetString(), Is.EqualTo("場所A"));
        var detail = await Get(Id(p));
        Assert.That(detail.GetProperty("totalQuantity").GetInt64(), Is.EqualTo(7));
        Assert.That(Etag(detail), Is.EqualTo(Etag(result)));
        var itemResponse = await client.GetAsync($"/api/v1/products/{Id(p):D}/items/{item.GetProperty("id").GetGuid():D}");
        Assert.That(itemResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(itemResponse.Headers.ETag!.ToString(), Is.EqualTo(Etag(result)));
    }

    [Test]
    public async Task ExtractAllAndReturnAllKeepTotalAndZeroRemovesLotOnly()
    {
        var p = await Product(1);
        var extracted = await Success(await Commands(p, [new { type = "extractItems", lotRef = LotId(p), itemRefs = new[] { "item" } }]));
        Assert.That(extracted.GetProperty("lots").GetArrayLength(), Is.Zero);
        Assert.That(extracted.GetProperty("totalQuantity").GetInt64(), Is.EqualTo(1));
        var itemId = extracted.GetProperty("createdRefs").GetProperty("item").GetString()!;
        var returned = await Success(await Commands(p, [new { type = "returnItem", itemRef = itemId, newLotRef = "lot", memoAction = "discardConfirmed", photoAction = "deleteConfirmed" }], etag: Etag(extracted)));
        var zero = await Success(await Commands(p, [new { type = "updateLot", lotRef = returned.GetProperty("createdRefs").GetProperty("lot").GetString(), quantity = 0 }], etag: Etag(returned)));
        Assert.That(zero.GetProperty("totalQuantity").GetInt64(), Is.Zero);
        var detail = await Get(Id(p));
        Assert.That(detail.GetProperty("links").GetArrayLength(), Is.EqualTo(1));
        Assert.That(detail.GetProperty("name").GetString(), Is.EqualTo("P05商品"));
        await Db(async db => Assert.That((await db.Set<Product>().SingleAsync()).DeletedAt, Is.Null));
    }

    [Test]
    public async Task OmittedFieldsRemainAndExplicitNullClearsWithoutChangingQuantity()
    {
        var p = await Product();
        var result = await Success(await Commands(p, [new { type = "updateLot", lotRef = LotId(p), fields = new { storageLocation = (string?)null, memo = "" } }]));
        var lot = result.GetProperty("lots")[0];
        Assert.That(lot.GetProperty("storageLocation").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(lot.GetProperty("memo").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(lot.GetProperty("purchasePlace").GetString(), Is.EqualTo("購入先A"));
        Assert.That(lot.GetProperty("condition").GetString(), Is.EqualTo("unopened"));
        Assert.That(result.GetProperty("totalQuantity").GetInt64(), Is.EqualTo(3));
    }

    [Test]
    public async Task InvalidLastCommandRollsBackEarlierWritesReferencesAndRevisions()
    {
        var p = await Product();
        var before = await Revision();
        var key = Guid.NewGuid();
        var failed = await Commands(p, [
            new { type = "createLot", clientRef = "new", quantity = 2, fields = Fields() },
            new { type = "extractItems", lotRef = "new", itemRefs = new[] { "item" } },
            new { type = "splitLot", lotRef = "new", quantity = 10, newLotRef = "bad" }
        ], key);
        Assert.That(failed.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await Revision(), Is.EqualTo(before));
        Assert.That(Etag(await Get(Id(p))), Is.EqualTo(Etag(p)));
        var after = await Inventory(Id(p));
        Assert.That(after.GetProperty("lots").GetArrayLength(), Is.EqualTo(1));
        Assert.That(after.GetProperty("items").GetArrayLength(), Is.Zero);
        await Db(async db => Assert.That(await db.Set<IdempotencyRecord>().AnyAsync(x => x.Key == key), Is.False));
    }

    [Test]
    public async Task ForeignProductRefsForwardRefsAndDuplicateDefinitionsAreRejectedAtomically()
    {
        var a = await Product();
        var b = await Product();
        object[][] invalid = [
            [new { type = "updateLot", lotRef = LotId(b), quantity = 2 }],
            [new { type = "updateLot", lotRef = "later", quantity = 2 }, new { type = "createLot", clientRef = "later", quantity = 2, fields = Fields() }],
            [new { type = "extractItems", lotRef = LotId(a), itemRefs = new[] { "same", "same" } }],
            [new { type = "extractItems", lotRef = LotId(a), itemRefs = new[] { "item" } }, new { type = "updateLot", lotRef = "item", quantity = 2 }],
            [new { type = "splitLot", lotRef = LotId(a), quantity = 3, newLotRef = "split" }],
            [new { type = "extractItems", lotRef = LotId(a), itemRefs = new[] { "a", "b", "c", "d" } }],
            [new { type = "extractItems", lotRef = LotId(a), itemRefs = new[] { "item" } }, new { type = "returnItem", itemRef = "item", newLotRef = "lot", memoAction = "missing", photoAction = "deleteConfirmed" }]
        ];
        var before = await Revision();
        foreach (var commands in invalid) Assert.That((await Commands(a, commands)).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await Revision(), Is.EqualTo(before));
        Assert.That((await Inventory(Id(a))).GetProperty("totalQuantity").GetInt64(), Is.EqualTo(3));
    }

    [Test]
    public async Task NoOpPreservesProductRevisionTimestampAndDataRevision()
    {
        var p = await Product();
        var before = await Revision();
        app.Clock.Advance(TimeSpan.FromMinutes(1));
        var result = await Success(await Commands(p, [new { type = "updateLot", lotRef = LotId(p), quantity = 3, fields = new { memo = "元ロットメモ" } }]));
        Assert.That(Etag(result), Is.EqualTo(Etag(p)));
        Assert.That(await Revision(), Is.EqualTo(before));
        Assert.That((await Get(Id(p))).GetProperty("updatedAt").GetString(), Is.EqualTo(p.GetProperty("updatedAt").GetString()));
    }

    [Test]
    public async Task ConcurrentWritesAndStaleAreaSnapshotsAllowOnlyOneCommit()
    {
        var p = await Product();
        var first = new object[] { new { type = "updateLot", lotRef = LotId(p), quantity = 4 } };
        var second = new object[] { new { type = "updateLot", lotRef = LotId(p), fields = new { memo = "changed" } } };
        var before = await Revision();
        var responses = await Task.WhenAll(Commands(p, first), Commands(p, second));
        Assert.That(responses.Select(x => x.StatusCode), Is.EquivalentTo(new[] { HttpStatusCode.OK, HttpStatusCode.PreconditionFailed }));
        Assert.That(await Revision(), Is.EqualTo(before + 1));
        foreach (var condition in new[] { "", "*", "W/" + Etag(p), Etag(p) + "," + Etag(p) })
        {
            var response = await Commands(p, first, etag: condition);
            Assert.That((int)response.StatusCode, Is.EqualTo(condition.Length == 0 ? 428 : 412));
        }
    }

    [Test]
    public async Task ReplayReturnsOriginalSuccessAfterLaterWriteAndDifferentConditionsConflict()
    {
        var p = await Product();
        var key = Guid.NewGuid();
        var commands = new object[] { new { type = "extractItems", lotRef = LotId(p), itemRefs = new[] { "item" } } };
        var original = await Commands(p, commands, key);
        var result = await Success(original);
        await Success(await Commands(p, [new { type = "updateLot", lotRef = LotId(p), quantity = 9 }], etag: Etag(result)));
        var before = await Revision();
        var replay = await Commands(p, commands, key);
        Assert.That(await replay.Content.ReadAsStringAsync(), Is.EqualTo(await original.Content.ReadAsStringAsync()));
        Assert.That(replay.Headers.ETag!.ToString(), Is.EqualTo(Etag(result)));
        Assert.That(await Revision(), Is.EqualTo(before));
        Assert.That((await Commands(p, commands, key, Etag(result))).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That((await Commands(p, [new { type = "updateLot", lotRef = LotId(p), quantity = 9 }], key)).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task SuccessRecordFailureRollsBackMutationAndExactRetryCanSucceed()
    {
        var p = await Product();
        var before = await Revision();
        await Db(db => db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_product_record BEFORE INSERT ON IdempotencyRecords BEGIN SELECT RAISE(ABORT,'test failure'); END"));
        var key = Guid.NewGuid();
        var commands = new object[] { new { type = "extractItems", lotRef = LotId(p), itemRefs = new[] { "item" } } };
        Assert.That((await Commands(p, commands, key)).StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That(await Revision(), Is.EqualTo(before));
        Assert.That(Etag(await Get(Id(p))), Is.EqualTo(Etag(p)));
        Assert.That((await Inventory(Id(p))).GetProperty("items").GetArrayLength(), Is.Zero);
        await Db(db => db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_product_record"));
        await Success(await Commands(p, commands, key));
    }

    [Test]
    public async Task RequestLimitsDoNotLimitPersistedLotOrItemCounts()
    {
        var p = await Product(101);
        var lots = Enumerable.Range(0, 100).Select(i => (object)new { type = "createLot", clientRef = "lot" + i, quantity = 1, fields = Fields() }).ToArray();
        var created = await Success(await Commands(p, lots));
        Assert.That(created.GetProperty("lots").GetArrayLength(), Is.EqualTo(101));
        var extracted = await Success(await Commands(p, [new { type = "extractItems", lotRef = LotId(p), itemRefs = Enumerable.Range(0, 100).Select(i => "i" + i).ToArray() }], etag: Etag(created)));
        var last = await Success(await Commands(p, [new { type = "extractItems", lotRef = LotId(p), itemRefs = new[] { "last" } }], etag: Etag(extracted)));
        Assert.That(last.GetProperty("items").GetArrayLength(), Is.EqualTo(101));
        Assert.That(last.GetProperty("totalQuantity").GetInt64(), Is.EqualTo(201));
        var before = await Revision();
        Assert.That((await Commands(p, lots.Append(lots[0]).ToArray(), etag: Etag(last))).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await Commands(p, [new { type = "extractItems", lotRef = LotId(p), itemRefs = Enumerable.Range(0, 101).Select(i => "x" + i).ToArray() }], etag: Etag(last))).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await Revision(), Is.EqualTo(before));
    }

    [TestCase("{\"commands\":[],\"extra\":1}")]
    [TestCase("{\"commands\":[{\"type\":\"createLot\",\"clientRef\":\"a\",\"quantity\":1.0,\"fields\":{}}]}")]
    [TestCase("{\"commands\":[{\"type\":\"createLot\",\"clientRef\":\"a\",\"quantity\":\"1\",\"fields\":{}}]}")]
    [TestCase("{\"commands\":[{\"type\":\"createLot\",\"clientRef\":\"a\",\"quantity\":2147483648,\"fields\":{}}]}")]
    [TestCase("{\"commands\":[{\"type\":\"createLot\",\"type\":\"createLot\",\"clientRef\":\"a\",\"quantity\":1,\"fields\":{}}]}")]
    [TestCase("{\"commands\":[{\"type\":\"unknownImageCommand\",\"itemRef\":\"a\",\"entries\":[]}]}")]
    [TestCase("{\"commands\":[{\"type\":\"updateItem\",\"itemRef\":\"\\uD800\",\"fields\":{}}]}")]
    public async Task InvalidJsonTypesAndUnknownFieldsDoNotMutate(string raw)
    {
        var p = await Product(); var before = await Revision();
        Assert.That((await Send($"products/{Id(p):D}/inventory-commands", raw, Etag(p))).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await Revision(), Is.EqualTo(before));
    }

    [Test]
    public async Task InvalidCreationAndBoundaryTextsAreRejectedWithoutPartialData()
    {
        var before = await Revision();
        foreach (var draft in new[] {
            Draft([new { scheme = "isbn10", value = "0306406153" }]),
            Draft([new { scheme = "isbn10", value = "0306406152" }, new { scheme = "isbn10", value = "０３０６４０６１５２" }]),
            Draft(works: [Guid.NewGuid()]) })
            Assert.That((await Send("dev/products", CreateBody(product: draft))).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await Revision(), Is.EqualTo(before));
        await Db(async db => Assert.That(await db.Set<Product>().CountAsync(), Is.Zero));
        var body = JsonSerializer.SerializeToNode(CreateBody())!;
        body["product"]!["name"] = string.Concat(Enumerable.Repeat("😀", 200));
        var response = await Send("dev/products", body);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        body["product"]!["name"] = string.Concat(Enumerable.Repeat("😀", 201));
        Assert.That((await Send("dev/products", body)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        body["product"]!["name"] = "valid";
        body["initialLot"]!["fields"]!["storageLocation"] = new string('a', 201);
        Assert.That((await Send("dev/products", body)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        body["initialLot"]!["fields"]!["storageLocation"] = null;
        body["initialLot"]!["fields"]!["memo"] = new string('a', 4001);
        Assert.That((await Send("dev/products", body)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task OverflowAndDatabaseConstraintsKeepParentAndChildrenUnchanged()
    {
        var p = await Product(int.MaxValue);
        await Db(async db =>
        {
            Assert.That(db.Database.HasPendingModelChanges(), Is.False);
            foreach (var sql in new[] { "UPDATE InventoryLots SET Quantity=0", "UPDATE InventoryLots SET Quantity=1.5", "UPDATE InventoryLots SET Quantity=2147483648", "UPDATE InventoryLots SET Condition='unknown'", "UPDATE InventoryLots SET Memo=char(0)", "UPDATE Products SET Revision=0", "UPDATE Products SET CategoryId='bad'", "UPDATE Products SET Name='　'" })
                Assert.ThrowsAsync<SqliteException>(async () => await db.Database.ExecuteSqlRawAsync(sql));
            await db.Database.ExecuteSqlRawAsync("UPDATE SystemState SET DataRevision=9223372036854775807");
        });
        Assert.That((await Commands(p, [new { type = "extractItems", lotRef = LotId(p), itemRefs = new[] { "one" } }])).StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That(Etag(await Get(Id(p))), Is.EqualTo(Etag(p)));
        Assert.That((await Inventory(Id(p))).GetProperty("totalQuantity").GetInt64(), Is.EqualTo(int.MaxValue));
    }

    [Test]
    public async Task AuthenticationCsrfSizeAndMissingKeyAreEnforced()
    {
        var p = await Product();
        using var anonymous = Client(app);
        Assert.That((await anonymous.GetAsync($"/api/v1/products/{Id(p):D}/inventory")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await anonymous.PostAsJsonAsync("/api/v1/dev/products", CreateBody())).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await client.PostAsJsonAsync("/api/v1/dev/products", CreateBody())).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await Send("dev/products", CreateBody(), includeKey: false)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await Send("dev/products", new string(' ', ProductWrites.BodyLimit) + "{}")).StatusCode, Is.EqualTo(HttpStatusCode.RequestEntityTooLarge));
        Assert.That((await Send("dev/products", "{broken")).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await Send("products", CreateBody())).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest)); // 正式登録は画像配列等を含むP08契約で検証
    }

    [TestCase("1.0")]
    [TestCase("1e0")]
    [TestCase("-1")]
    [TestCase("\"1\"")]
    [TestCase("null")]
    [TestCase("2147483648")]
    public async Task QuantityInputRejectsNonIntegerJsonAndOutOfRangeValues(string quantity)
    {
        var p = await Product();
        var before = await Revision();
        var raw = "{\"commands\":[{\"type\":\"updateLot\",\"lotRef\":\"" + LotId(p) + "\",\"quantity\":" + quantity + "}]}";
        Assert.That((await Send($"products/{Id(p):D}/inventory-commands", raw, Etag(p))).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await Revision(), Is.EqualTo(before));
    }

    [Test]
    public async Task FailedCreationSuccessRecordLeavesNoProductCodeRelationOrLot()
    {
        var before = await Revision();
        await Db(db => db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_create_record BEFORE INSERT ON IdempotencyRecords BEGIN SELECT RAISE(ABORT,'test failure'); END"));
        var body = CreateBody(product: Draft([new { scheme = "isbn10", value = "0306406152" }]));
        var key = Guid.NewGuid();
        Assert.That((await Send("dev/products", body, key: key)).StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That(await Revision(), Is.EqualTo(before));
        await Db(async db =>
        {
            Assert.That(await db.Set<Product>().CountAsync(), Is.Zero);
            Assert.That(await db.Set<ProductIdentifier>().CountAsync(), Is.Zero);
            Assert.That(await db.Set<ProductLink>().CountAsync(), Is.Zero);
            Assert.That(await db.Set<InventoryLot>().CountAsync(), Is.Zero);
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_create_record");
        });
        Assert.That((await Send("dev/products", body, key: key)).StatusCode, Is.EqualTo(HttpStatusCode.Created));
    }

    [Test]
    public async Task SuccessfulCreationReplayHasFixedSevenDayExpiry()
    {
        await SendAsync(client, "auth/password", new { loginId = "owner", password = Password, rememberMe = true });
        var body = CreateBody();
        var key = Guid.NewGuid();
        var original = await Send("dev/products", body, key: key);
        app.Clock.Advance(TimeSpan.FromDays(6));
        var replay = await Send("dev/products", body, key: key);
        Assert.That(await replay.Content.ReadAsStringAsync(), Is.EqualTo(await original.Content.ReadAsStringAsync()));
        app.Clock.Advance(TimeSpan.FromDays(1));
        var fresh = await Send("dev/products", body, key: key);
        Assert.That(fresh.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(fresh.Headers.Location, Is.Not.EqualTo(original.Headers.Location));
        await Db(async db => Assert.That(await db.Set<Product>().CountAsync(), Is.EqualTo(2)));
    }

    [Test]
    public async Task MasterUsageCountsProductsOnceAndSeparatesTrash()
    {
        var w1 = new Work { Id = Guid.NewGuid(), Name = "w1", NormalizedName = "w1" };
        var w2 = new Work { Id = Guid.NewGuid(), Name = "w2", NormalizedName = "w2" };
        var series = new Series { Id = Guid.NewGuid(), Name = "s", NormalizedName = "s" };
        var character = new Character { Id = Guid.NewGuid(), Name = "c", NormalizedName = "c" };
        var tag = new Tag { Id = Guid.NewGuid(), Name = "t", NormalizedName = "t" };
        var org = new Organization { Id = Guid.NewGuid(), Name = "o", NormalizedName = "o" };
        await Db(async db =>
        {
            db.AddRange(w1, w2, series, character, tag, org);
            db.AddRange(new WorkSeries { WorkId = w1.Id, SeriesId = series.Id }, new WorkSeries { WorkId = w2.Id, SeriesId = series.Id });
            await db.SaveChangesAsync();
        });
        var draft = Draft(works: [w1.Id, w2.Id], characters: [character.Id], tags: [tag.Id], org: org.Id);
        await Product(3, draft);
        var trashed = await Product(2, draft);
        await Db(async db => { (await db.Set<Product>().SingleAsync(x => x.Id == Id(trashed))).DeletedAt = app.Clock.GetUtcNow(); await db.SaveChangesAsync(); });
        foreach (var (kind, id) in new[] { ("works", w1.Id), ("series", series.Id), ("characters", character.Id), ("tags", tag.Id), ("organizations", org.Id), ("categories", category) })
        {
            var master = await client.GetFromJsonAsync<JsonElement>($"/api/v1/masters/{kind}/{id:D}");
            var usage = master.GetProperty("usage");
            Assert.That(usage.GetProperty("activeProductCount").GetInt64(), Is.EqualTo(1), kind);
            Assert.That(usage.GetProperty("trashedProductCount").GetInt64(), Is.EqualTo(1), kind);
        }
        Assert.That((await client.GetAsync($"/api/v1/products/{Id(trashed):D}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await Commands(trashed, [new { type = "updateLot", lotRef = LotId(trashed), quantity = 3 }])).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

}
