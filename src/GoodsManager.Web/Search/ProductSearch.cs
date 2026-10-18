using System.Data.Common;
using System.Globalization;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Products;
using GoodsManager.Web.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoodsManager.Web.Search;

public sealed record NamedRef(Guid Id, string Name);
public sealed record ProductSummary(Guid Id, string Name, NamedRef Category, NamedRef[] Works, long TotalQuantity, string?[] Conditions, string[] StorageLocations, object? PrimaryImage, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string Revision);
public sealed record SearchPage(ProductSummary[] Items, int Page, int PageSize, long TotalCount, long TotalPages, string DataRevision);

/// <summary>SQLの構造は固定の許可リスト、利用者の値はすべてパラメーター。各読取は呼出側のスナップショットに参加。</summary>
public sealed class ProductSearch(AppDbContext db)
{
    internal sealed record Plan(string From, string Where, string Rank, Dictionary<string, object> Parameters);
    internal static Plan Build(SearchQuery query)
    {
        var values = new Dictionary<string, object>();
        string Param(object value) { var key = "@v" + values.Count; values.Add(key, value); return key; }
        string In(IEnumerable<string> source) => string.Join(',', source.Select(x => Param(x)));
        string Ids(string key) => In(query.Ids.GetValueOrDefault(key, []).Select(StorageValues.FormatId));
        var where = new List<string> { "p.DeletedAt IS NULL" };
        var cte = "";
        if (query.Ids.GetValueOrDefault("categoryIds", []).Length > 0)
        {
            cte = $"WITH RECURSIVE selectedCategories(Id) AS (SELECT CategoryId FROM Categories WHERE CategoryId IN ({Ids("categoryIds")}) UNION SELECT c.CategoryId FROM Categories c JOIN selectedCategories s ON c.ParentCategoryId=s.Id) ";
            where.Add("p.CategoryId IN (SELECT Id FROM selectedCategories)");
        }
        var from = cte + "SELECT p.ProductId, p.Name, p.CategoryId, c.Name AS CategoryName, p.CreatedAt, p.UpdatedAt, p.Revision, p.NormalizedName, " +
            "COALESCE((SELECT SUM(CAST(l.Quantity AS INTEGER)) FROM InventoryLots l WHERE l.ProductId=p.ProductId),0) + (SELECT COUNT(*) FROM InventoryItems i WHERE i.ProductId=p.ProductId) AS TotalQuantity " +
            "FROM Products p JOIN Categories c ON c.CategoryId=p.CategoryId";
        var codeChecks = new List<string>();
        foreach (var code in query.Terms.Select(SearchQuery.Comparable).Append(SearchQuery.Comparable(query.Q)).OfType<string>().Distinct())
            codeChecks.Add($"EXISTS(SELECT 1 FROM ProductIdentifiers i WHERE i.ProductId=p.ProductId AND i.ComparisonValue={Param(code)})");
        var textChecks = new Dictionary<string, List<string>> { ["NameNormalized"] = [], ["WorksNormalized"] = [], ["CharactersNormalized"] = [], ["OtherNormalized"] = [] };
        if (query.Terms.Length > 0)
        {
            from += " JOIN ProductSearchDocuments d ON d.ProductId=p.ProductId";
            foreach (var term in query.Terms)
            {
                var parameter = Param(term);
                var checks = new List<string>();
                foreach (var field in textChecks.Keys) { var match = $"instr(d.{field},{parameter})>0"; checks.Add(match); textChecks[field].Add(match); }
                var code = SearchQuery.Comparable(term);
                if (code is not null) checks.Add($"EXISTS(SELECT 1 FROM ProductIdentifiers i WHERE i.ProductId=p.ProductId AND i.ComparisonValue={Param(code)})");
                var wholeCode = SearchQuery.Comparable(query.Q);
                if (wholeCode is not null) checks.Add($"EXISTS(SELECT 1 FROM ProductIdentifiers i WHERE i.ProductId=p.ProductId AND i.ComparisonValue={Param(wholeCode)})");
                // instrで%・_・バックスラッシュも文字通りに比較する。
                where.Add("(" + string.Join(" OR ", checks) + ")");
            }
        }
        if (query.Barcode is not null) where.Add($"EXISTS(SELECT 1 FROM ProductIdentifiers i WHERE i.ProductId=p.ProductId AND i.ComparisonValue={Param(query.Barcode)})");
        foreach (var (key, table, column) in new[] { ("workIds", "ProductWorks", "WorkId"), ("characterIds", "ProductCharacters", "CharacterId"), ("tagIds", "ProductTags", "TagId") })
            if (query.Ids.GetValueOrDefault(key, []).Length > 0) where.Add($"EXISTS(SELECT 1 FROM {table} r WHERE r.ProductId=p.ProductId AND r.{column} IN ({Ids(key)}))");
        if (query.Ids.GetValueOrDefault("seriesIds", []).Length > 0) where.Add($"EXISTS(SELECT 1 FROM ProductWorks w JOIN WorkSeries s ON s.WorkId=w.WorkId WHERE w.ProductId=p.ProductId AND s.SeriesId IN ({Ids("seriesIds")}))");
        foreach (var (key, column) in new[] { ("manufacturerIds", "ManufacturerId"), ("sellerIds", "SellerId") })
            if (query.Ids.GetValueOrDefault(key, []).Length > 0) where.Add($"p.{column} IN ({Ids(key)})");
        if (query.Conditions.Length > 0 || query.StorageLocation is not null || query.PurchasePlace is not null)
        {
            var holding = new List<string> { "h.ProductId=p.ProductId" };
            if (query.Conditions.Length > 0)
            {
                var set = query.Conditions.Where(x => x != "unset").ToArray();
                var check = set.Length > 0 ? $"h.Condition IN ({In(set)})" : "0";
                if (query.Conditions.Contains("unset")) check += " OR h.Condition IS NULL";
                holding.Add("(" + check + ")");
            }
            if (query.StorageLocation is not null) holding.Add("h.StorageLocationNormalized=" + Param(query.StorageLocation));
            if (query.PurchasePlace is not null) holding.Add("h.PurchasePlaceNormalized=" + Param(query.PurchasePlace));
            var clause = string.Join(" AND ", holding);
            where.Add($"(EXISTS(SELECT 1 FROM InventoryLots h WHERE {clause}) OR EXISTS(SELECT 1 FROM InventoryItems h WHERE {clause}))");
        }
        // P07の共通画像テーブル接続前は、商品画像を持つ正本が存在しない。
        if (query.HasImages == true) where.Add("0");
        var rank = "5";
        if (query.Terms.Length > 0)
        {
            var cases = new List<string>();
            if (codeChecks.Count > 0) cases.Add("WHEN " + string.Join(" OR ", codeChecks) + " THEN 0");
            cases.Add("WHEN d.NameNormalized=" + Param(query.Q) + " THEN 1");
            var priority = 2;
            foreach (var field in new[] { "NameNormalized", "WorksNormalized", "CharactersNormalized" }) cases.Add("WHEN " + string.Join(" OR ", textChecks[field]) + " THEN " + priority++);
            rank = "CASE " + string.Join(' ', cases) + " ELSE 5 END";
        }
        var selectEnd = from.IndexOf(" FROM Products", StringComparison.Ordinal);
        from = from.Insert(selectEnd, ", " + rank + " AS SearchRank");
        var filters = new List<string>();
        if (query.Inventory == "positive") filters.Add("TotalQuantity>0");
        if (query.Inventory == "zero") filters.Add("TotalQuantity=0");
        if (query.QuantityMin.HasValue) filters.Add("TotalQuantity>=" + Param(query.QuantityMin.Value));
        if (query.QuantityMax.HasValue) filters.Add("TotalQuantity<=" + Param(query.QuantityMax.Value));
        var sql = "SELECT * FROM (" + from + " WHERE " + string.Join(" AND ", where) + ") found";
        return new(sql, filters.Count == 0 ? "" : " WHERE " + string.Join(" AND ", filters), rank, values);
    }
    private DbCommand Command(string text, IReadOnlyDictionary<string, object> parameters)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("検索は読取トランザクション内で行う");
        var command = db.Database.GetDbConnection().CreateCommand(); command.CommandText = text;
        command.Transaction = db.Database.CurrentTransaction.GetDbTransaction();
        foreach (var value in parameters) command.Parameters.Add(new SqliteParameter(value.Key, value.Value));
        return command;
    }
    public async Task<SearchPage> PageAsync(SearchQuery query, CancellationToken token)
    {
        var plan = Build(query);
        await using var countCommand = Command("SELECT COUNT(*) FROM (" + plan.From + plan.Where + ")", plan.Parameters);
        var count = Convert.ToInt64(await countCommand.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
        var pages = (count + query.PageSize - 1) / query.PageSize;
        var page = (int)Math.Min(query.Page, Math.Max(1, pages));
        var order = query.Sort switch
        {
            "relevance" => "SearchRank ASC, CreatedAt DESC", "createdAsc" => "CreatedAt ASC", "updatedDesc" => "UpdatedAt DESC",
            "nameAsc" => "NormalizedName COLLATE BINARY ASC", "nameDesc" => "NormalizedName COLLATE BINARY DESC",
            "quantityDesc" => "TotalQuantity DESC", "quantityAsc" => "TotalQuantity ASC", _ => "CreatedAt DESC"
        };
        var parameters = new Dictionary<string, object>(plan.Parameters) { ["@limit"] = query.PageSize, ["@offset"] = ((long)page - 1) * query.PageSize };
        await using var command = Command(plan.From + plan.Where + " ORDER BY " + order + ", ProductId ASC LIMIT @limit OFFSET @offset", parameters);
        var rows = new List<Row>();
        await using (var reader = await command.ExecuteReaderAsync(token))
            while (await reader.ReadAsync(token)) rows.Add(new(StorageValues.ParseId(reader.GetString(0)), reader.GetString(1), new(StorageValues.ParseId(reader.GetString(2)), reader.GetString(3)), StorageValues.FromMilliseconds(reader.GetInt64(4)), StorageValues.FromMilliseconds(reader.GetInt64(5)), reader.GetInt64(6), StorageValues.RequireSafeTotal(reader.GetInt64(8))));
        var items = await Summaries(rows, token);
        var revision = (await db.SystemStates.AsNoTracking().SingleAsync(token)).DataRevision.ToString(CultureInfo.InvariantCulture);
        return new(items, page, query.PageSize, count, pages, revision);
    }
    private sealed record Row(Guid Id, string Name, NamedRef Category, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Revision, long Total);
    private async Task<ProductSummary[]> Summaries(List<Row> rows, CancellationToken token)
    {
        var ids = rows.Select(x => x.Id).ToArray();
        var works = await (from r in db.Set<ProductWork>() join w in db.Set<Work>() on r.WorkId equals w.Id where ids.Contains(r.ProductId) orderby w.NormalizedName, w.Id select new { r.ProductId, w.Id, w.Name }).ToArrayAsync(token);
        var lots = await db.Set<InventoryLot>().AsNoTracking().Where(x => ids.Contains(x.ProductId)).Select(x => new { x.ProductId, x.Condition, x.StorageLocation }).ToArrayAsync(token);
        var items = await db.Set<InventoryItem>().AsNoTracking().Where(x => ids.Contains(x.ProductId)).Select(x => new { x.ProductId, x.Condition, x.StorageLocation }).ToArrayAsync(token);
        var own = lots.Concat(items).ToArray();
        return rows.Select(r => new ProductSummary(r.Id, r.Name, r.Category, works.Where(w => w.ProductId == r.Id).Select(w => new NamedRef(w.Id, w.Name)).ToArray(), r.Total,
            own.Where(x => x.ProductId == r.Id).Select(x => x.Condition).Distinct().Order(StringComparer.Ordinal).ToArray(),
            own.Where(x => x.ProductId == r.Id && x.StorageLocation is not null).Select(x => x.StorageLocation!).Distinct().Order(StringComparer.Ordinal).ToArray(), null, r.CreatedAt, r.UpdatedAt, r.Revision.ToString(CultureInfo.InvariantCulture))).ToArray();
    }
    public async Task<object> SummaryAsync(string inventory, CancellationToken token)
    {
        var query = new SearchQuery { Inventory = inventory, PageSize = 25 };
        var plan = Build(query);
        await using var command = Command("SELECT COUNT(*), COALESCE(SUM(TotalQuantity),0) FROM (" + plan.From + plan.Where + ")", plan.Parameters);
        long count, total;
        await using (var reader = await command.ExecuteReaderAsync(token)) { await reader.ReadAsync(token); count = reader.GetInt64(0); total = StorageValues.RequireSafeTotal(reader.GetInt64(1)); }
        var page = await PageAsync(query, token);
        return new { productCount = count, totalQuantity = total, recentProducts = page.Items.Take(10).ToArray(), page.DataRevision };
    }
    public async Task<string[]> SuggestionsAsync(string field, string text, int limit, CancellationToken token)
    {
        var column = field == "storageLocation" ? "StorageLocation" : "PurchasePlace";
        var normalized = column + "Normalized";
        // 正本の現存値だけ。削除済み商品は通常の候補へ出さない。
        var sql = $"SELECT DISTINCT Value FROM (SELECT h.{column} AS Value, h.{normalized} AS Normalized FROM InventoryLots h JOIN Products p ON p.ProductId=h.ProductId WHERE p.DeletedAt IS NULL UNION ALL SELECT h.{column}, h.{normalized} FROM InventoryItems h JOIN Products p ON p.ProductId=h.ProductId WHERE p.DeletedAt IS NULL) WHERE Value IS NOT NULL AND instr(Normalized,@q)>0 ORDER BY Normalized COLLATE BINARY, Value COLLATE BINARY LIMIT @limit";
        await using var command = Command(sql, new Dictionary<string, object> { ["@q"] = MasterText.Normalize(text), ["@limit"] = limit });
        var result = new List<string>(); await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(reader.GetString(0));
        return result.ToArray();
    }
}
