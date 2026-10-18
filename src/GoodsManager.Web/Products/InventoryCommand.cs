using System.Text.Json;
using GoodsManager.Web.Images;

namespace GoodsManager.Web.Products;

public abstract record InventoryCommand;
public sealed record CreateLot(string ClientRef, int Quantity, OwnershipPatch Fields) : InventoryCommand;
public sealed record UpdateLot(string LotRef, int? Quantity, OwnershipPatch Fields) : InventoryCommand;
public sealed record SplitLot(string LotRef, int Quantity, string NewLotRef, OwnershipPatch Fields) : InventoryCommand;
public sealed record ExtractItems(string LotRef, string[] ItemRefs, OwnershipPatch Fields) : InventoryCommand;
public sealed record UpdateItem(string ItemRef, OwnershipPatch Fields) : InventoryCommand;
public sealed record ReplaceItemImages(string ItemRef, ImageEditEntry[] Entries) : InventoryCommand;
public sealed record ReturnItem(string ItemRef, string NewLotRef, string MemoAction, string PhotoAction) : InventoryCommand;

public static class InventoryCommands
{
    public static InventoryCommand[] Parse(JsonElement body)
    {
        var root = new InputObject(body, "commands");
        var raw = root.Array("commands");
        if (raw.Length is < 1 or > 100) throw InputObject.Invalid("commands");
        var commands = raw.Select(ParseOne).ToArray();
        if (commands.OfType<ExtractItems>().Sum(x => (long)x.ItemRefs.Length) > 100) throw InputObject.Invalid("itemRefs");
        if (commands.OfType<ReplaceItemImages>().Sum(x => (long)x.Entries.Count(e => e.TemporaryUploadId.HasValue)) > 100) throw InputObject.Invalid("entries");
        return commands;
    }

    private static InventoryCommand ParseOne(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("type", out var discriminator) || discriminator.ValueKind != JsonValueKind.String) throw InputObject.Invalid("type");
        var type = discriminator.GetString();
        var allowed = type switch
        {
            "createLot" => new[] { "type", "clientRef", "quantity", "fields" },
            "updateLot" => ["type", "lotRef", "quantity", "fields"],
            "splitLot" => ["type", "lotRef", "quantity", "newLotRef", "fields"],
            "extractItems" => ["type", "lotRef", "itemRefs", "fields"],
            "updateItem" => ["type", "itemRef", "fields"],
            "returnItem" => ["type", "itemRef", "newLotRef", "memoAction", "photoAction"],
            "replaceItemImages" => ["type", "itemRef", "entries"],
            _ => throw InputObject.Invalid("type")
        };
        var x = new InputObject(body, allowed);
        var fields = x.Has("fields") ? InputObject.Fields(x.Get("fields"), type == "createLot") : OwnershipPatch.Empty;
        return type switch
        {
            "createLot" => x.Has("fields") ? new CreateLot(x.Text("clientRef"), x.Integer("quantity"), fields) : throw InputObject.Invalid("fields"),
            "updateLot" => x.Has("quantity") || x.Has("fields") ? new UpdateLot(x.Text("lotRef"), x.Has("quantity") ? x.Integer("quantity", 0) : null, fields) : throw InputObject.Invalid(),
            "splitLot" => new SplitLot(x.Text("lotRef"), x.Integer("quantity"), x.Text("newLotRef"), fields),
            "extractItems" => new ExtractItems(x.Text("lotRef"), x.Array("itemRefs").Select(v => v.ValueKind == JsonValueKind.String ? v.GetString()! : throw InputObject.Invalid("itemRefs")).ToArray(), fields),
            "updateItem" => x.Has("fields") ? new UpdateItem(x.Text("itemRef"), fields) : throw InputObject.Invalid("fields"),
            "replaceItemImages" => new ReplaceItemImages(x.Text("itemRef"), ImageEditEntry.Parse(x.Get("entries"))),
            "returnItem" => new ReturnItem(x.Text("itemRef"), x.Text("newLotRef"), x.Text("memoAction"), x.Text("photoAction")),
            _ => throw InputObject.Invalid("type")
        };
    }
}
