using System.Globalization;
using System.Net;
using System.Text.Json;

namespace GoodsManager.SampleData;

public sealed record ImportResult(int Added, int Skipped);

/// <summary>通常APIでサンプルだけを追加し、既存データや編集済みサンプルを上書きしない。</summary>
public sealed class SampleDataImporter(HttpClient client, SampleCatalog catalog, string stateDirectory, TextWriter output)
{
    private readonly SampleApi api = new(client);

    public async Task<ImportResult> RunAsync()
    {
        var origin = LocalTarget.Parse(client.BaseAddress?.AbsoluteUri ?? "");
        var session = await api.ReadAsync("auth/session");
        if (!client.DefaultRequestHeaders.Contains("X-Collection-Id")) throw new InvalidOperationException("投入先のコレクションを指定してください。");
        var userId = session.GetProperty("user").GetProperty("id").GetString()!;
        using var journal = new ImportJournal(stateDirectory, origin, userId, api);
        var existing = await ExistingAsync();
        var pending = catalog.Products.Where(product => !existing.ContainsKey(product.Id)
            || journal.HasRequest(Label(product)) && !journal.IsComplete(Label(product))).ToArray();
        var skipped = catalog.Products.Length - pending.Length;
        if (pending.Length == 0) return new(0, skipped);

        var masters = await MastersAsync(journal);
        var added = 0;
        foreach (var source in pending)
        {
            var label = Label(source);
            JsonElement saved;
            if (journal.HasRequest(label))
            {
                // 応答を失った登録は、保存してある本文とキーで結果を回収する。
                saved = await journal.WriteAsync(label, "products", new { });
                var id = saved.GetProperty("product").GetProperty("id").GetGuid();
                if (existing.TryGetValue(source.Id, out var found) && found != id)
                    throw new InvalidOperationException("実行記録と既存サンプルのIDが一致しません。");
                if (!existing.ContainsKey(source.Id))
                {
                    // DB復元・手動削除・メモ編集を、未登録と誤認して再作成しない。
                    try { await api.ReadAsync($"products/{id}"); }
                    catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.NotFound)
                    {
                        try { await api.ReadAsync($"trash/products/{id}"); }
                        catch (HttpRequestException missing) when (missing.StatusCode == HttpStatusCode.NotFound)
                        { throw new InvalidOperationException("実行記録の商品が接続先にありません。DB復元・削除の状況を確認してください。", missing); }
                    }
                }
                if (journal.IsComplete(label)) { skipped++; continue; }
            }
            else saved = await CreateAsync(source, masters, journal);

            var product = saved.GetProperty("product");
            await FinishAsync(source, product, journal);
            await journal.CompleteAsync(label);
            added++;
            await output.WriteLineAsync($"追加・再開 {added}/{pending.Length}: {source.Name}");
        }
        return new(added, skipped);
    }

    private static string Label(SampleProduct product) => "product:" + product.Id.ToString(CultureInfo.InvariantCulture);

    private async Task<Dictionary<int, Guid>> ExistingAsync()
    {
        var query = "q=" + Uri.EscapeDataString(SampleCatalog.Marker);
        var rows = (await api.AllAsync("products?inventory=all&" + query)).Concat(await api.AllAsync("trash/products?" + query));
        var existing = new Dictionary<int, Guid>();
        foreach (var row in rows)
        {
            var id = row.GetProperty("id").GetGuid();
            var trashed = row.GetProperty("deletedAt").ValueKind != JsonValueKind.Null;
            var detail = await api.ReadAsync((trashed ? "trash/" : "") + $"products/{id}");
            var memo = detail.GetProperty("memo").GetString() ?? "";
            foreach (var sample in catalog.Products.Where(sample => memo.StartsWith(SampleCatalog.ProductMarker(sample.Id) + "\n", StringComparison.Ordinal)))
                if (!existing.TryAdd(sample.Id, id)) throw new InvalidOperationException($"サンプル番号 {sample.Id} が複数あります。先にデータを確認してください。");
        }
        return existing;
    }

    private async Task<Dictionary<string, Dictionary<string, Guid>>> MastersAsync(ImportJournal journal)
    {
        var masters = new Dictionary<string, Dictionary<string, Guid>>();
        foreach (var (label, kind) in new[] { ("作品", "works"), ("シリーズ", "series"), ("キャラクター", "characters"), ("タグ", "tags"), ("組織", "organizations") })
        {
            var values = new Dictionary<string, Guid>();
            foreach (var name in catalog.MasterData[label]) values[name] = await EnsureAsync(kind, name, journal);
            masters[kind] = values;
        }
        var parents = new Dictionary<string, string>
        {
            ["アクリルスタンド"] = "アクリル・キーホルダー類",
            ["アクリルキーホルダー"] = "アクリル・キーホルダー類",
            ["ぬいぐるみ"] = "フィギュア・立体物",
            ["クリアファイル"] = "紙・印刷物",
            ["ポストカード"] = "紙・印刷物",
            ["缶バッジ"] = "バッジ類",
            ["マグカップ・グラス"] = "食器・生活雑貨"
        };
        var categories = new Dictionary<string, Guid>();
        foreach (var name in catalog.Products.Select(product => product.Category).Distinct())
        {
            Guid? parent = parents.TryGetValue(name, out var parentName) ? await EnsureAsync("categories", parentName, journal) : null;
            categories[name] = await EnsureAsync("categories", name, journal, parent);
        }
        masters["categories"] = categories;
        foreach (var (work, series) in new[] { ("星巡りの旅", "星巡りシリーズ"), ("星巡りの旅（表記違い）", "星巡りシリーズ"), ("月と喫茶室", "喫茶室シリーズ") })
            await RelateAsync("works", masters["works"][work], masters["series"][series], journal);
        foreach (var (character, work) in new[] { ("蒼", "星巡りの旅"), ("リク", "星巡りの旅"), ("ルナ", "月と喫茶室"), ("ハル", "花のある日々") })
            await RelateAsync("characters", masters["characters"][character], masters["works"][work], journal);
        return masters;
    }

    private async Task<Guid> EnsureAsync(string kind, string name, ImportJournal journal, Guid? parent = null)
    {
        var matches = (await api.AllAsync($"masters/{kind}?q={Uri.EscapeDataString(name)}"))
            .Where(item => item.GetProperty("name").GetString() == name
                && (kind != "categories" || (item.GetProperty("parentId").ValueKind == JsonValueKind.Null ? (Guid?)null : item.GetProperty("parentId").GetGuid()) == parent)).ToArray();
        if (matches.Length > 1) throw new InvalidOperationException($"同名のマスターが複数あります: {name}");
        if (matches.Length == 1) return matches[0].GetProperty("id").GetGuid();
        object body = kind switch
        {
            "organizations" => new { name, websiteUrl = "https://example.com/local-demo", memo = "架空の組織。" + SampleCatalog.Marker },
            "categories" => new { name, parentId = parent, sortOrder = 0 },
            _ => new { name }
        };
        var value = await journal.WriteAsync("master:" + kind + ":" + name, "masters/" + kind, body);
        var id = value.GetProperty("id").GetGuid();
        await api.ReadAsync($"masters/{kind}/{id}");
        return id;
    }

    private async Task RelateAsync(string kind, Guid id, Guid peer, ImportJournal journal)
    {
        var state = await api.ReadAsync($"masters/{kind}/{id}/relations");
        var relations = await api.AllAsync($"masters/{kind}/{id}/relations");
        if (relations.Any(item => item.GetProperty("id").GetGuid() == peer)) return;
        await journal.WriteAsync($"relation:{kind}:{id}:{peer}", $"masters/{kind}/{id}/relations", new { addIds = new[] { peer }, removeIds = Array.Empty<Guid>() }, state.GetProperty("masterEtag").GetString());
    }

    private async Task<JsonElement> CreateAsync(SampleProduct source, Dictionary<string, Dictionary<string, Guid>> masters, ImportJournal journal)
    {
        var index = source.Id - 1; var label = Label(source);
        var images = new List<Guid>();
        if (index % 13 != 12) images.Add(await journal.UploadAsync(label, source.ImageFile));
        if (index % 10 == 0) images.Add(await journal.UploadAsync(label + ":second", catalog.Products[(Array.IndexOf(catalog.Products, source) + 1) % catalog.Products.Length].ImageFile));
        var tags = new List<Guid>();
        if (index % 3 == 0) tags.Add(masters["tags"]["会場限定"]);
        if (index % 5 == 0) tags.Add(masters["tags"]["お気に入り"]);
        if (index % 11 == 0) tags.Add(masters["tags"]["要整理"]);
        var fields = new
        {
            condition = new string?[] { "unopened", "opened", "inUse", null }[index % 4],
            storageLocation = new[] { "書斎 / グッズボックスA", "リビング / 飾り棚", "書斎 / 本棚B" }[index % 3],
            purchasePlace = new[] { "星巡り展 会場ショップ", "公式オンラインショップ", "ミュージアムショップ" }[index % 3],
            memo = index % 8 == 0 ? "購入時の外袋も一緒に保管。" : null
        };
        var product = new
        {
            source.Name,
            categoryId = masters["categories"][source.Category],
            version = index % 9 == 0 ? "展覧会ver." : "通常ver.",
            manufacturerId = masters["organizations"]["星空企画"],
            sellerId = masters["organizations"][new[] { "星空企画", "星の博物館", "月灯舎" }[index % 3]],
            memo = SampleCatalog.ProductMarker(source.Id) + "\n" + source.Memo,
            workIds = source.Works.Select(name => masters["works"][name]).ToArray(),
            characterIds = source.Characters.Select(name => masters["characters"][name]).ToArray(),
            tagIds = tags.ToArray(),
            identifiers = new[] { new { scheme = "ean13", value = SampleCatalog.Barcode(source.Id) } },
            links = new[] { new { url = $"https://example.com/local-demo/products/{source.Id}" } }
        };
        return await journal.WriteAsync(label, "products", new { product, initialLot = new { quantity = Quantity(source), fields }, images = images.Select(id => new { temporaryUploadId = id }).ToArray(), duplicateConfirmation = (object?)null });
    }

    private static int Quantity(SampleProduct source) => Math.Max(source.Lots[0].Quantity, (source.Id - 1) % 7 == 0 ? 2 : 1);

    private async Task FinishAsync(SampleProduct source, JsonElement product, ImportJournal journal)
    {
        var index = source.Id - 1; var label = Label(source); var id = product.GetProperty("id").GetGuid();
        var etag = product.GetProperty("etag").GetString();
        if (index % 17 == 16 || index % 7 == 0)
        {
            JsonElement result;
            if (journal.HasRequest(label + ":inventory")) result = await journal.WriteAsync(label + ":inventory", $"products/{id}/inventory-commands", new { });
            else
            {
                var lotId = product.GetProperty("inventory").GetProperty("lots")[0].GetProperty("id").GetString();
                var commands = new List<object>();
                if (index % 17 == 16) commands.Add(new { type = "updateLot", lotRef = lotId, quantity = 0 });
                else
                {
                    commands.Add(new { type = "extractItems", lotRef = lotId, itemRefs = new[] { "display-item" }, fields = new { condition = "opened", storageLocation = "リビング / 飾り棚" } });
                    commands.Add(new { type = "updateItem", itemRef = "display-item", fields = new { memo = "展示用の個体。台座裏にサンプル番号のメモ。" } });
                    if (Quantity(source) >= 3) commands.Add(new { type = "splitLot", lotRef = lotId, quantity = 1, newLotRef = "spare", fields = new { condition = "unopened", storageLocation = "書斎 / 予備ボックス" } });
                    var image = await journal.UploadAsync(label + ":item", source.ImageFile);
                    commands.Add(new { type = "replaceItemImages", itemRef = "display-item", entries = new[] { new { clientRef = "item-photo", temporaryUploadId = image, sortOrder = 0 } } });
                }
                result = await journal.WriteAsync(label + ":inventory", $"products/{id}/inventory-commands", new { commands }, etag);
            }
            etag = result.GetProperty("etag").GetString();
        }
        if (index >= 62) await journal.WriteAsync(label + ":trash", $"products/{id}", new { }, etag, "DELETE");
    }
}
