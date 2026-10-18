using System.Text.Json;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Storage;

namespace GoodsManager.Web.Products;

/// <summary>未指定と明示nullを区別する所持属性の部分更新。</summary>
public readonly record struct Supplied<T>(bool HasValue, T Value);
public sealed record OwnershipPatch(Supplied<string?> Condition, Supplied<string?> StorageLocation, Supplied<string?> PurchasePlace, Supplied<string?> Memo)
{
    public static OwnershipPatch Empty { get; } = new(default, default, default, default);
    public bool Apply(Ownership target)
    {
        var before = (target.Condition, target.StorageLocation, target.PurchasePlace, target.Memo);
        if (Condition.HasValue) target.Condition = Condition.Value;
        if (StorageLocation.HasValue) target.StorageLocation = StorageLocation.Value;
        if (PurchasePlace.HasValue) target.PurchasePlace = PurchasePlace.Value;
        if (Memo.HasValue) target.Memo = Memo.Value;
        return before != (target.Condition, target.StorageLocation, target.PurchasePlace, target.Memo);
    }
}

public sealed record IdentifierInput(string Scheme, string Value);
public sealed record ProductDraft(string Name, Guid CategoryId, string? Version, Guid? ManufacturerId, Guid? SellerId, string? Memo,
    Guid[] WorkIds, Guid[] CharacterIds, Guid[] TagIds, IdentifierInput[] Identifiers, string[] Links);

/// <summary>JSONの未知/重複キーと不正型を拒否。コードポイントの上限を共有する。</summary>
public sealed class InputObject
{
    private readonly Dictionary<string, JsonElement> values = new(StringComparer.Ordinal);
    public InputObject(JsonElement source, params string[] allowed)
    {
        if (source.ValueKind != JsonValueKind.Object) throw Invalid();
        foreach (var p in source.EnumerateObject())
            if (!allowed.Contains(p.Name) || !values.TryAdd(p.Name, p.Value)) throw Invalid(p.Name);
    }
    public bool Has(string name) => values.ContainsKey(name);
    public JsonElement Get(string name) => values.TryGetValue(name, out var value) ? value : throw Invalid(name);
    public string Text(string name, int maximum = 200) => Get(name).ValueKind == JsonValueKind.String && StorageValues.IsValidText(Get(name).GetString(), 1, maximum) ? Get(name).GetString()! : throw Invalid(name);
    public string? NullableText(string name, int maximum = 200, bool trim = true)
    {
        var value = Get(name);
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String || !StorageValues.IsValidText(value.GetString(), 0, maximum)) throw Invalid(name);
        var text = trim ? value.GetString()!.Trim() : value.GetString()!;
        return text.Length == 0 ? null : text;
    }
    public int Integer(string name, int minimum = 1)
    {
        var value = Get(name);
        // JSON 1.0/1e0、文字列の整数も整数入力の代用にはしない。
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number < minimum) throw Invalid(name);
        return number;
    }
    public Guid Id(string name)
    {
        var text = Text(name);
        return Guid.TryParseExact(text, "D", out var id) && id != Guid.Empty ? id : throw Invalid(name);
    }
    public Guid? OptionalId(string name) => !Has(name) || Get(name).ValueKind == JsonValueKind.Null ? null : Id(name);
    public JsonElement[] Array(string name)
    {
        var value = Get(name);
        return value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : throw Invalid(name);
    }
    public Guid[] Ids(string name)
    {
        var result = Array(name).Select(x => x.ValueKind == JsonValueKind.String && Guid.TryParseExact(x.GetString(), "D", out var id) && id != Guid.Empty ? id : throw Invalid(name)).ToArray();
        if (result.Distinct().Count() != result.Length) throw Invalid(name);
        return result;
    }
    public static MasterProblem Invalid(string? field = null) => new(400, "VALIDATION_FAILED", field);
    public static OwnershipPatch Fields(JsonElement source, bool complete = false)
    {
        var input = new InputObject(source, "condition", "storageLocation", "purchasePlace", "memo");
        if (complete && new[] { "condition", "storageLocation", "purchasePlace", "memo" }.Any(x => !input.Has(x))) throw Invalid("fields");
        string? Condition()
        {
            var condition = input.Get("condition").ValueKind == JsonValueKind.Null ? null : input.Text("condition");
            return condition is null or "unopened" or "opened" or "inUse" ? condition : throw Invalid("condition");
        }
        return new(input.Has("condition") ? new(true, Condition()) : default,
            input.Has("storageLocation") ? new(true, input.NullableText("storageLocation")) : default,
            input.Has("purchasePlace") ? new(true, input.NullableText("purchasePlace")) : default,
            input.Has("memo") ? new(true, input.NullableText("memo", 4000, trim: false)) : default);
    }

    public static ProductDraft Product(JsonElement source)
    {
        var x = new InputObject(source, "name", "categoryId", "version", "manufacturerId", "sellerId", "memo", "workIds", "characterIds", "tagIds", "identifiers", "links");
        return new(x.Text("name").Trim(), x.Id("categoryId"),
            x.Has("version") ? x.NullableText("version") : null, x.OptionalId("manufacturerId"), x.OptionalId("sellerId"),
            x.Has("memo") ? x.NullableText("memo", 4000, trim: false) : null,
            x.Ids("workIds"), x.Ids("characterIds"), x.Ids("tagIds"),
            x.Array("identifiers").Select(v => { var i = new InputObject(v, "scheme", "value"); return new IdentifierInput(i.Text("scheme"), i.Text("value")); }).ToArray(),
            x.Array("links").Select(v => { var i = new InputObject(v, "url"); return i.Text("url", 2048); }).ToArray());
    }
}
