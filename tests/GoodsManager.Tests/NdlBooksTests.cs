using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoodsManager.Web.Books;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static GoodsManager.Tests.AuthenticationTests;

namespace GoodsManager.Tests;

public sealed class NdlBooksTests
{
    private const string Isbn = "9784101010137";
    private const string Item = """
        <item><dc:title>こころ</dc:title><dcndl:volume>上</dcndl:volume><dcndl:edition>改版</dcndl:edition>
        <dc:creator>夏目, 漱石, 1867-1916</dc:creator><dc:publisher>新潮社</dc:publisher><dc:date>2004</dc:date>
        <dc:identifier xsi:type="dcndl:ISBN13">4-10-101013-7</dc:identifier>
        <link>https://ndlsearch.ndl.go.jp/books/R100000002-I000007577929</link></item>
        """;
    private static string Rss(string items = Item, int total = 1, int index = 1) => $$"""
        <rss xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcndl="http://ndl.go.jp/dcndl/terms/" xmlns:openSearch="http://a9.com/-/spec/opensearchrss/1.0/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
        <channel><openSearch:totalResults>{{total}}</openSearch:totalResults><openSearch:startIndex>{{index}}</openSearch:startIndex>{{items}}</channel></rss>
        """;
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public List<string> Calls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls.Add(request.RequestUri!.AbsoluteUri); return send(request, cancellationToken); }
    }
    private static HttpResponseMessage Xml(string xml) => new(HttpStatusCode.OK) { Content = new StringContent(xml, Encoding.UTF8, "application/xml") };

    [Test]
    public async Task PreservesVolumeEditionYearAndNormalizesReturnedIsbnByValueNotAttribute()
    {
        var handler = new Handler((_, _) => Task.FromResult(Xml(Rss())));
        using var books = new NdlBooks(new HttpClient(handler), new TestClock());
        var record = (await books.FindAsync(Isbn, default)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(record.Title, Is.EqualTo("こころ 上"));
            Assert.That(record.Volume, Is.EqualTo("上"));
            Assert.That(record.Edition, Is.EqualTo("改版"));
            Assert.That(record.Authors, Is.EqualTo(new[] { "夏目 漱石" }));
            Assert.That(record.PublicationYear, Is.EqualTo("2004"));
            Assert.That(record.InProcess, Is.False);
        });
        await books.FindAsync("４-１０-１０１０１３-７", default);
        Assert.That(handler.Calls, Has.Count.EqualTo(1));
        Assert.That(handler.Calls[0], Does.Contain("dpid=iss-ndl-opac&isbn=" + Isbn));
    }

    [Test]
    public async Task KeepsVolumeEvenWhenTheBaseTitleEndsWithTheSameCharacterAndAcceptsYearOnlyIssued()
    {
        foreach (var (title, expected) in new[] { ("地上", "地上 上"), ("こころ 上", "こころ 上"), ("こころ（上）", "こころ（上）") })
        {
            var xml = Rss(Item.Replace("<dc:title>こころ</dc:title>", $"<dc:title>{title}</dc:title>").Replace("<dc:date>2004</dc:date>", "<issued xmlns='http://purl.org/dc/terms/'>2004.3</issued>"));
            var handler = new Handler((_, _) => Task.FromResult(Xml(xml))); using var books = new NdlBooks(new HttpClient(handler), new TestClock());
            var record = (await books.FindAsync(Isbn, default)).Single(); Assert.That(record.Title, Is.EqualTo(expected)); Assert.That(record.PublicationYear, Is.EqualTo("2004.3"));
        }
    }

    [Test]
    public async Task OnlyAnExplicitEmptyResultSearchesInProcessAndCachesThatResultBriefly()
    {
        var handler = new Handler((request, _) => Task.FromResult(Xml(request.RequestUri!.Query.Contains("inprocess") ? Rss() : Rss("", 0))));
        var clock = new TestClock(); using var books = new NdlBooks(new HttpClient(handler), clock);
        Assert.That((await books.FindAsync(Isbn, default)).Single().InProcess, Is.True);
        await books.FindAsync(Isbn, default); Assert.That(handler.Calls, Has.Count.EqualTo(2));
        clock.Advance(TimeSpan.FromMinutes(31)); await books.FindAsync(Isbn, default); Assert.That(handler.Calls, Has.Count.EqualTo(4));
    }

    [TestCase("isbn")]
    [TestCase("4006381333931")]
    [TestCase("9784101010138")]
    [TestCase("978410101013")]
    [TestCase("")]
    public void InvalidIsbnNeverCallsTheExternalService(string isbn)
    {
        var handler = new Handler((_, _) => throw new AssertionException("No HTTP expected")); using var books = new NdlBooks(new HttpClient(handler), new TestClock());
        Assert.That(Assert.ThrowsAsync<MasterProblem>(() => books.FindAsync(isbn, default))!.Code, Is.EqualTo("INVALID_ISBN"));
        Assert.That(handler.Calls, Is.Empty);
    }

    [TestCase("<html>maintenance</html>")]
    [TestCase("<rss><channel/></rss>")]
    [TestCase("<!DOCTYPE rss [<!ENTITY x SYSTEM 'file:///not-read'>]><rss>&x;</rss>")]
    public void MalformedResponsesNeverFallBackOrBecomeNotFound(string xml)
    {
        var handler = new Handler((_, _) => Task.FromResult(Xml(xml))); using var books = new NdlBooks(new HttpClient(handler), new TestClock());
        Assert.That(Assert.ThrowsAsync<MasterProblem>(() => books.FindAsync(Isbn, default))!.Code, Is.EqualTo("BOOK_LOOKUP_UNAVAILABLE"));
        Assert.That(handler.Calls, Has.Count.EqualTo(1));
    }

    [Test]
    public void InconsistentCountsMismatchedIsbnAndUnsafeLinksAreFailures()
    {
        foreach (var xml in new[] { Rss("", 1), Rss(Item, 0), Rss(Item.Replace("4-10-101013-7", "0-306-40615-2")), Rss(Item.Replace("dcndl:ISBN13", "dcndl:NDLBibID")), Rss(Item.Replace("https://ndlsearch.ndl.go.jp/books/", "https://example.com/")), Rss(Item, 501) })
        {
            var handler = new Handler((_, _) => Task.FromResult(Xml(xml))); using var books = new NdlBooks(new HttpClient(handler), new TestClock());
            Assert.That(Assert.ThrowsAsync<MasterProblem>(() => books.FindAsync(Isbn, default))!.Status, Is.EqualTo(502));
            Assert.That(handler.Calls, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task IgnoresErrorIsbnHitsWhenAnActualMatchingRecordAlsoExists()
    {
        var xml = Rss(Item.Replace("dcndl:ISBN13", "dcndl:ErrorISBN") + Item, 2);
        var handler = new Handler((_, _) => Task.FromResult(Xml(xml))); using var books = new NdlBooks(new HttpClient(handler), new TestClock());
        Assert.That(await books.FindAsync(Isbn, default), Has.Length.EqualTo(1)); Assert.That(handler.Calls, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task FailuresAreNotCachedAndConcurrentEquivalentIsbnsShareOneUpstreamRequest()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler(async (_, token) => { entered.TrySetResult(); await release.Task.WaitAsync(token); return Xml(Rss()); });
        using var books = new NdlBooks(new HttpClient(handler), new TestClock());
        var first = books.FindAsync(Isbn, default); await entered.Task; var second = books.FindAsync("4101010137", default);
        Assert.That(handler.Calls, Has.Count.EqualTo(1)); release.SetResult(); await Task.WhenAll(first, second); Assert.That(handler.Calls, Has.Count.EqualTo(1));
        var errors = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var unavailable = new NdlBooks(new HttpClient(errors), new TestClock());
        for (var i = 0; i < 2; i++) Assert.ThrowsAsync<MasterProblem>(() => unavailable.FindAsync(Isbn, default));
        Assert.That(errors.Calls, Has.Count.EqualTo(2));
    }

    [Test]
    public void CancellationAndTimeoutNeverTriggerFallback()
    {
        var handler = new Handler((_, _) => throw new OperationCanceledException()); using var books = new NdlBooks(new HttpClient(handler), new TestClock());
        Assert.That(Assert.ThrowsAsync<MasterProblem>(() => books.FindAsync(Isbn, default))!.Code, Is.EqualTo("BOOK_LOOKUP_TIMEOUT"));
        Assert.That(handler.Calls, Has.Count.EqualTo(1));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); Assert.That(async () => await books.FindAsync(Isbn, cancelled.Token), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(handler.Calls, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task FetchesEveryPageBeforeOfferingCandidates()
    {
        var items = string.Concat(Enumerable.Range(1, 100).Select(i => Item.Replace("I000007577929", "I" + i)));
        var handler = new Handler((request, _) => Task.FromResult(Xml(request.RequestUri!.Query.Contains("idx=101") ? Rss(Item, 101, 101) : Rss(items, 101))));
        using var books = new NdlBooks(new HttpClient(handler), new TestClock());
        Assert.That(await books.FindAsync(Isbn, default), Has.Length.EqualTo(101)); Assert.That(handler.Calls, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task RequiresAuthenticationAndResolvesAllExactPublisherMatchesFreshlyWithoutWritingMasters()
    {
        var handler = new Handler((_, _) => Task.FromResult(Xml(Rss())));
        await using var app = new TestApplication { ConfigureTestServices = services => { services.RemoveAll<NdlBooks>(); services.AddSingleton(sp => new NdlBooks(new HttpClient(handler), sp.GetRequiredService<TimeProvider>())); } };
        using var client = Client(app);
        Assert.That((await client.GetAsync("/api/v1/book-metadata?isbn=" + Isbn)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized)); Assert.That(handler.Calls, Is.Empty);
        await SetupAsync(client);
        async Task Add(string name)
        {
            await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Add(new Organization { Id = Guid.NewGuid(), Name = name, NormalizedName = MasterText.Normalize(name), CreatedAt = app.Clock.GetUtcNow(), UpdatedAt = app.Clock.GetUtcNow(), WebsiteUrl = "https://example.com/" }); await db.SaveChangesAsync();
        }
        for (var i = 0; i < 101; i++) await Add("新潮社 " + i);
        await Add("新潮社"); await Add("新潮社　");
        var response = await client.GetFromJsonAsync<JsonElement>("/api/v1/book-metadata?isbn=" + Isbn);
        var matches = response.GetProperty("items")[0].GetProperty("publishers")[0].GetProperty("matches"); Assert.That(matches.GetArrayLength(), Is.EqualTo(2));
        await Add("新潮社"); response = await client.GetFromJsonAsync<JsonElement>("/api/v1/book-metadata?isbn=" + Isbn);
        Assert.That(response.GetProperty("items")[0].GetProperty("publishers")[0].GetProperty("matches").GetArrayLength(), Is.EqualTo(3)); Assert.That(handler.Calls, Has.Count.EqualTo(1));
        await using var verifyScope = app.Services.CreateAsyncScope(); Assert.That(await verifyScope.ServiceProvider.GetRequiredService<AppDbContext>().Set<Organization>().CountAsync(), Is.EqualTo(104));
    }
}
