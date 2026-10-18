using System.Globalization;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Products;
using GoodsManager.Web.Storage;
using Microsoft.Extensions.Primitives;

namespace GoodsManager.Web.Search;

public sealed record SearchQuery
{
    public string Q { get; init; } = "";
    public string[] Terms { get; init; } = [];
    public string? Barcode { get; init; }
    public Dictionary<string, Guid[]> Ids { get; init; } = [];
    public string[] Conditions { get; init; } = [];
    public string? StorageLocation { get; init; }
    public string? PurchasePlace { get; init; }
    public bool? HasImages { get; init; }
    public long? QuantityMin { get; init; }
    public long? QuantityMax { get; init; }
    public string Inventory { get; init; } = "positive";
    public string Sort { get; init; } = "createdDesc";
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public static readonly string[] IdFields = ["workIds", "seriesIds", "characterIds", "categoryIds", "tagIds", "manufacturerIds", "sellerIds"];
    public static readonly string[] Sorts = ["relevance", "createdDesc", "createdAsc", "updatedDesc", "nameAsc", "nameDesc", "quantityDesc", "quantityAsc"];

    public static SearchQuery Parse(IQueryCollection query)
    {
        string Text(string field)
        {
            var values = query[field];
            if (values.Count > 1 || !StorageValues.IsValidText(values.ToString(), 0, 200)) throw MasterInput.Invalid(field);
            return values.ToString();
        }
        string Choice(string field, string fallback, string[] choices)
        {
            if (!query.ContainsKey(field)) return fallback;
            var value = Text(field);
            return choices.Contains(value, StringComparer.Ordinal) ? value : throw MasterInput.Invalid(field);
        }
        long? Number(string field)
        {
            if (!query.ContainsKey(field)) return null;
            var text = Text(field);
            return text.Length > 0 && text.All(char.IsAsciiDigit) && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number <= StorageValues.MaximumSafeInteger ? number : throw MasterInput.Invalid(field);
        }
        var normalized = MasterText.Normalize(Text("q"));
        var barcodeText = Text("barcode");
        var barcode = barcodeText.Length == 0 ? null : Comparable(barcodeText) ?? throw MasterInput.Invalid("barcode");
        var ids = new Dictionary<string, Guid[]>();
        foreach (var field in IdFields)
        {
            StringValues values = query[field];
            if (values.Count > 100) throw MasterInput.Invalid(field);
            ids[field] = values.Select(x => Guid.TryParseExact(x, "D", out var id) && id != Guid.Empty ? id : throw MasterInput.Invalid(field)).Distinct().ToArray();
        }
        var conditions = query["conditions"].Select(x => x ?? "").Distinct().ToArray();
        if (query["conditions"].Count > 100 || conditions.Any(x => x is not ("unopened" or "opened" or "inUse" or "unset"))) throw MasterInput.Invalid("conditions");
        var min = Number("quantityMin"); var max = Number("quantityMax");
        if (min > max) throw MasterInput.Invalid("quantityMax");
        var page = Number("page") ?? 1; var size = Number("pageSize") ?? 50;
        if (page is < 1 or > int.MaxValue || size is not (25 or 50 or 100)) throw MasterInput.Invalid("page");
        var sort = Choice("sort", normalized.Length == 0 ? "createdDesc" : "relevance", Sorts);
        if (normalized.Length == 0 && sort == "relevance") sort = "createdDesc";
        var image = query.ContainsKey("hasImages") ? Choice("hasImages", "", ["true", "false"]) : null;
        return new SearchQuery
        {
            Q = normalized, Terms = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray(), Barcode = barcode, Ids = ids, Conditions = conditions,
            StorageLocation = NullIfEmpty(MasterText.Normalize(Text("storageLocation"))), PurchasePlace = NullIfEmpty(MasterText.Normalize(Text("purchasePlace"))),
            HasImages = image is null ? null : image == "true", QuantityMin = min, QuantityMax = max,
            Inventory = Choice("inventory", "positive", ["positive", "all", "zero"]), Sort = sort, Page = (int)page, PageSize = (int)size
        };
    }
    public static string? Comparable(string text)
    {
        foreach (var scheme in new[] { "ean8", "upca", "ean13", "isbn10", "isbn13" })
            if (IdentifierCodes.TryNormalize(scheme, text, out var code)) return code.ComparisonValue;
        return null;
    }
    private static string? NullIfEmpty(string text) => text.Length == 0 ? null : text;
}
