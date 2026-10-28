using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Products;
using GoodsManager.Web.Storage;

namespace GoodsManager.Web.Books;

/// <summary>NDL作成書誌だけを検索し、画面へ渡す書名と出典を保持する。</summary>
public sealed record BookRecord(string Title, string Volume, string Edition, string[] Authors, string[] Publishers, string PublicationYear, string SourceUrl, bool InProcess);

/// <summary>外部通信を直列化し、同値のISBN-10/13を同じキャッシュで扱う。失敗はキャッシュしない。</summary>
public sealed class NdlBooks(HttpClient client, TimeProvider clock) : IDisposable
{
    private const int PageSize = 100;
    private const int ResponseLimit = 2 * 1024 * 1024;
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Ndl = "http://ndl.go.jp/dcndl/terms/";
    private static readonly XNamespace Search = "http://a9.com/-/spec/opensearchrss/1.0/";
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, (DateTimeOffset Expires, BookRecord[] Records)> cache = [];

    public static string NormalizeIsbn(string? isbn)
    {
        if (!StorageValues.IsValidText(isbn, 1, 200)) throw new MasterProblem(400, "INVALID_ISBN", "isbn");
        if (!IdentifierCodes.TryNormalize("isbn10", isbn, out var value) && !IdentifierCodes.TryNormalize("isbn13", isbn, out value))
            throw new MasterProblem(400, "INVALID_ISBN", "isbn");
        return value!.ComparisonValue[1..];
    }

    public async Task<BookRecord[]> FindAsync(string? isbn, CancellationToken cancellationToken)
    {
        var key = NormalizeIsbn(isbn);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue(key, out var hit) && hit.Expires > clock.GetUtcNow()) return hit.Records;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            BookRecord[] records;
            try
            {
                records = await SearchAsync(key, false, timeout.Token);
                if (records.Length == 0) records = await SearchAsync(key, true, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new MasterProblem(504, "BOOK_LOOKUP_TIMEOUT"); }
            catch (Exception error) when (error is HttpRequestException or XmlException or InvalidDataException or IOException)
            { throw new MasterProblem(502, "BOOK_LOOKUP_UNAVAILABLE"); }
            if (cache.Count >= 128) cache.Remove(cache.MinBy(x => x.Value.Expires).Key);
            var lifetime = records.Length == 0 ? TimeSpan.FromMinutes(10) : records.Any(x => x.InProcess) ? TimeSpan.FromMinutes(30) : TimeSpan.FromHours(24);
            cache[key] = (clock.GetUtcNow() + lifetime, records);
            return records;
        }
        finally { gate.Release(); }
    }

    private async Task<BookRecord[]> SearchAsync(string isbn, bool inProcess, CancellationToken token)
    {
        var provider = inProcess ? "iss-ndl-opac-inprocess" : "iss-ndl-opac";
        var books = new List<BookRecord>();
        long? expectedTotal = null;
        for (var index = 1; ; index += PageSize)
        {
            // URLや検索対象は外部入力から組み立てず、検算済みISBNだけを送信する。
            using var response = await client.GetAsync($"https://ndlsearch.ndl.go.jp/api/opensearch?dpid={provider}&isbn={isbn}&cnt={PageSize}&idx={index}", HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > ResponseLimit) throw new InvalidDataException();
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var buffer = new MemoryStream();
            var bytes = new byte[8192];
            while (true)
            {
                var read = await stream.ReadAsync(bytes, token);
                if (read == 0) break;
                if (buffer.Length + read > ResponseLimit) throw new InvalidDataException();
                buffer.Write(bytes, 0, read);
            }
            buffer.Position = 0;
            using var reader = XmlReader.Create(buffer, new XmlReaderSettings { Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = ResponseLimit });
            var document = await XDocument.LoadAsync(reader, LoadOptions.None, token);
            var channel = document.Root?.Name == "rss" ? document.Root.Element("channel") : null;
            if (channel is null || !long.TryParse(channel.Element(Search + "totalResults")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var total)
                || total < 0 || total > 500 || expectedTotal.HasValue && total != expectedTotal
                || channel.Element(Search + "startIndex")?.Value != index.ToString(CultureInfo.InvariantCulture)) throw new InvalidDataException();
            expectedTotal = total;
            var items = channel.Elements("item").ToArray();
            if (items.Length != Math.Min(PageSize, Math.Max(0, total - index + 1))) throw new InvalidDataException();
            foreach (var item in items)
            {
                if (Parse(item, isbn, inProcess) is { } book) books.Add(book);
            }
            if (index + items.Length > total) break;
        }
        // 検索は誤ISBNの記録にも一致する。検算済みの一致書誌だけを使い、全件不一致を0件と扱わない。
        if (expectedTotal > 0 && books.Count == 0) throw new InvalidDataException();
        return books.DistinctBy(x => x.SourceUrl).ToArray();
    }

    private static BookRecord? Parse(XElement item, string isbn, bool inProcess)
    {
        // ISBN13という属性でISBN-10が返ることもあるため、属性でなく値を検算する。
        var matched = item.Elements(Dc + "identifier").Any(x =>
            ((string?)x.Attribute(XName.Get("type", "http://www.w3.org/2001/XMLSchema-instance")))?.Split(':').Last() is "ISBN" or "ISBN10" or "ISBN13" &&
            (IdentifierCodes.TryNormalize("isbn10", Clean(x.Value), out var value) || IdentifierCodes.TryNormalize("isbn13", Clean(x.Value), out value))
            && value!.ComparisonValue[1..] == isbn);
        if (!matched) return null;
        var title = Clean(item.Element(Dc + "title")?.Value ?? item.Element("title")?.Value);
        if (title.Length == 0) throw new InvalidDataException();
        var volume = Clean(item.Element(Ndl + "volume")?.Value);
        // 「地上」という書名と巻次「上」を、単純な末尾一致で同じ情報と見なさない。
        var volumeSuffixes = new[] { " " + volume, "." + volume, "．" + volume, ":" + volume, "：" + volume, "（" + volume + "）", "(" + volume + ")" };
        if (volume.Length > 0 && !volumeSuffixes.Any(x => title.EndsWith(x, StringComparison.Ordinal))) title += " " + volume;
        var url = Clean(item.Element("link")?.Value);
        if (!Regex.IsMatch(url, @"\Ahttps://ndlsearch\.ndl\.go\.jp/books/R100000002-I[A-Za-z0-9-]+\z", RegexOptions.CultureInvariant)) throw new InvalidDataException();
        var authors = item.Elements(Dc + "creator").Select(x => Clean(Regex.Replace(Clean(x.Value), @",\s*\d{4}(?:-\d{0,4})?$", "")).Replace(", ", " ", StringComparison.Ordinal)).Where(x => x.Length > 0).Distinct().ToArray();
        return new BookRecord(title, volume, Clean(item.Element(Ndl + "edition")?.Value), authors,
            item.Elements(Dc + "publisher").Select(x => Clean(x.Value)).Where(x => x.Length > 0).Distinct().ToArray(),
            Clean(item.Element(Dc + "date")?.Value ?? item.Element(XName.Get("issued", "http://purl.org/dc/terms/"))?.Value), url, inProcess);
    }

    private static string Clean(string? value) => Regex.Replace(value ?? "", @"\s+", " ").Trim();
    public void Dispose() { gate.Dispose(); client.Dispose(); }
}
