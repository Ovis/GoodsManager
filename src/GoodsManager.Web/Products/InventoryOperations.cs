using GoodsManager.Web.Masters;
using GoodsManager.Web.Images;
using GoodsManager.Web.Storage;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Products;

/// <summary>操作列を順に適用する。途中失敗時に呼出側がDB・更新番号・再送記録をまとめて取り消す。</summary>
public sealed class InventoryOperations(AppDbContext db, ProductOperations products, ImageLinks images)
{
    public async Task<IReadOnlyDictionary<string, Guid>> ApplyAsync(Product product, IReadOnlyList<InventoryCommand> commands, DateTimeOffset now, CancellationToken token, Guid? userId = null)
    {
        products.RequireTransaction();
        if (commands.Count is < 1 or > 100 || commands.OfType<ExtractItems>().Sum(x => (long)x.ItemRefs.Length) > 100) throw InputObject.Invalid("commands");
        var lots = await db.Set<InventoryLot>().Where(x => x.ProductId == product.Id).ToListAsync(token);
        var items = await db.Set<InventoryItem>().Where(x => x.ProductId == product.Id).ToListAsync(token);
        var before = ProductOperations.Total(lots, items);
        long delta = 0;
        var changed = false;
        var refs = new Dictionary<string, (Guid Id, bool Item)>(StringComparer.Ordinal);
        MasterProblem Invalid() => new(409, "INVALID_INVENTORY_OPERATION");
        Guid Resolve(string reference, bool item)
        {
            if (!StorageValues.IsValidText(reference, 1, 200)) throw Invalid();
            if (Guid.TryParseExact(reference, "D", out var existing)) return existing;
            return refs.TryGetValue(reference, out var entry) && entry.Item == item ? entry.Id : throw Invalid();
        }
        InventoryLot Lot(string reference) => lots.SingleOrDefault(x => x.Id == Resolve(reference, false)) ?? throw Invalid();
        InventoryItem Item(string reference) => items.SingleOrDefault(x => x.Id == Resolve(reference, true)) ?? throw Invalid();
        Guid Define(string reference, bool item)
        {
            if (!StorageValues.IsValidText(reference, 1, 200) || string.IsNullOrWhiteSpace(reference) || Guid.TryParse(reference, out _) || refs.ContainsKey(reference)) throw Invalid();
            var id = Guid.NewGuid();
            refs.Add(reference, (id, item));
            return id;
        }
        InventoryLot AddLot(string reference, int quantity, Ownership? source, OwnershipPatch fields)
        {
            if (quantity < 1) throw Invalid();
            ProductOperations.ValidateFields(fields);
            var lot = new InventoryLot { Id = Define(reference, false), ProductId = product.Id, Quantity = quantity, Condition = source?.Condition, StorageLocation = source?.StorageLocation, PurchasePlace = source?.PurchasePlace, Memo = source?.Memo, CreatedAt = now, UpdatedAt = now };
            fields.Apply(lot); lots.Add(lot); db.Add(lot); changed = true;
            return lot;
        }
        void RemoveLot(InventoryLot lot) { lots.Remove(lot); db.Remove(lot); changed = true; }
        void ChangeFields(Ownership entity, OwnershipPatch fields)
        {
            ProductOperations.ValidateFields(fields);
            if (fields.Apply(entity)) { entity.UpdatedAt = now; changed = true; }
        }

        foreach (var command in commands)
        {
            switch (command)
            {
                case CreateLot c:
                    AddLot(c.ClientRef, c.Quantity, null, c.Fields);
                    delta = checked(delta + c.Quantity);
                    break;
                case UpdateLot c:
                    {
                        var lot = Lot(c.LotRef);
                        ProductOperations.ValidateFields(c.Fields);
                        if (c.Quantity is < 0) throw Invalid();
                        if (c.Quantity.HasValue)
                        {
                            delta = checked(delta + c.Quantity.Value - lot.Quantity);
                            if (c.Quantity == 0) { RemoveLot(lot); break; }
                            if (lot.Quantity != c.Quantity) { lot.Quantity = c.Quantity.Value; lot.UpdatedAt = now; changed = true; }
                        }
                        ChangeFields(lot, c.Fields);
                        break;
                    }
                case SplitLot c:
                    {
                        var lot = Lot(c.LotRef);
                        if (c.Quantity < 1 || c.Quantity >= lot.Quantity) throw Invalid();
                        AddLot(c.NewLotRef, c.Quantity, lot, c.Fields);
                        lot.Quantity -= c.Quantity; lot.UpdatedAt = now;
                        break;
                    }
                case ExtractItems c:
                    {
                        var lot = Lot(c.LotRef);
                        if (c.ItemRefs.Length < 1 || c.ItemRefs.Length > lot.Quantity) throw Invalid();
                        ProductOperations.ValidateFields(c.Fields);
                        // 個体化時の固有メモは常に空欄。元ロットのメモを引き継がない。
                        if (c.Fields.Memo.HasValue) throw InputObject.Invalid("memo");
                        foreach (var reference in c.ItemRefs)
                        {
                            var item = new InventoryItem { Id = Define(reference, true), ProductId = product.Id, Condition = lot.Condition, StorageLocation = lot.StorageLocation, PurchasePlace = lot.PurchasePlace, Memo = null, CreatedAt = now, UpdatedAt = now };
                            c.Fields.Apply(item); items.Add(item); db.Add(item);
                        }
                        lot.Quantity -= c.ItemRefs.Length; lot.UpdatedAt = now; changed = true;
                        if (lot.Quantity == 0) RemoveLot(lot);
                        break;
                    }
                case UpdateItem c:
                    ChangeFields(Item(c.ItemRef), c.Fields);
                    break;
                case ReplaceItemImages c:
                    if (!userId.HasValue) throw Invalid();
                    changed |= await images.ReplaceItemAsync(Item(c.ItemRef).Id, userId.Value, c.Entries, token);
                    break;
                case ReturnItem c:
                    {
                        var item = Item(c.ItemRef);
                        if (c.MemoAction != "discardConfirmed" || c.PhotoAction is not ("moveToProduct" or "deleteConfirmed")) throw Invalid();
                        await images.MoveItemImagesAsync(item.Id, product.Id, c.PhotoAction == "moveToProduct", token);
                        AddLot(c.NewLotRef, 1, item, new(default, default, default, new(true, null)));
                        items.Remove(item); db.Remove(item);
                        break;
                    }
                default: throw Invalid();
            }
            if (ProductOperations.Total(lots, items) != checked(before + delta)) throw new InvalidOperationException("所持数量の不変条件に違反");
        }
        if (changed) await products.TouchAsync(product, now, token);
        return refs.ToDictionary(x => x.Key, x => x.Value.Id, StringComparer.Ordinal);
    }
}
