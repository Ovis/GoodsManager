using System.Security.Cryptography;
using System.Text.Json;
using GoodsManager.Web.Collections;
using GoodsManager.Web.Images;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Products;
using GoodsManager.Web.Search;
using GoodsManager.Web.Storage;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Samples;

/// <summary>新規の専用コレクションへ、同梱サンプルを一括作成する。HTTPや認証情報を介した自己呼出しは行わない。</summary>
public sealed class SampleCollectionSeeder(AppDbContext db, ProductOperations products, InventoryOperations inventory, SearchProjection projection, IImageFiles files)
{
    /// <summary>既存の商品検証・所持操作・検索投影を使う。失敗時は呼出側がすべてのDB変更を取り消す。</summary>
    public async Task SeedAsync(CollectionSpace space, DateTimeOffset now, CancellationToken token)
    {
        if (db.Database.CurrentTransaction is null || space.SampleStatus != "pending")
        {
            throw new InvalidOperationException("サンプル準備には未完了の専用コレクションとトランザクションが必要");
        }

        db.CollectionId = space.Id;
        if (await db.Set<Product>().AnyAsync(token))
        {
            throw new InvalidDataException("サンプル作成先に既存商品がある");
        }

        var catalog = SampleCatalog.Load();
        var masters = CreateMasters(catalog, now);
        await db.SaveChangesAsync(token);
        var assets = await CreateImagesAsync(now, token);
        var ids = new List<Guid>();
        foreach (var source in catalog.Products)
        {
            var index = source.Id - 1;
            var tags = new List<Guid>();
            if (index % 3 == 0)
            {
                tags.Add(masters["tags"]["会場限定"]);
            }

            if (index % 5 == 0)
            {
                tags.Add(masters["tags"]["お気に入り"]);
            }

            if (index % 11 == 0)
            {
                tags.Add(masters["tags"]["要整理"]);
            }

            var draft = new ProductDraft(source.Name, masters["categories"][source.Category], index % 9 == 0 ? "展覧会ver." : "通常ver.",
                masters["organizations"]["星空企画"], masters["organizations"][new[] { "星空企画", "星の博物館", "月灯舎" }[index % 3]],
                $"【{SampleCatalog.Marker} #{source.Id:000}】\n{source.Memo}",
                source.Works.Select(name => masters["works"][name]).ToArray(), source.Characters.Select(name => masters["characters"][name]).ToArray(), tags.ToArray(),
                [new IdentifierInput("ean13", SampleCatalog.Barcode(source.Id))], [$"https://example.com/sample/products/{source.Id}"]);
            var fields = new OwnershipPatch(new(true, new string?[] { "unopened", "opened", "inUse", null }[index % 4]),
                new(true, new[] { "書斎 / グッズボックスA", "リビング / 飾り棚", "書斎 / 本棚B" }[index % 3]),
                new(true, new[] { "星巡り展 会場ショップ", "公式オンラインショップ", "ミュージアムショップ" }[index % 3]),
                new(true, index % 8 == 0 ? "購入時の外袋も一緒に保管。" : null));
            var quantity = Math.Max(source.Lots[0].Quantity, index % 7 == 0 ? 2 : 1);
            var product = await products.CreateAsync(draft, quantity, fields, now, token);
            await db.SaveChangesAsync(token);
            var photos = new List<Guid>();
            if (index % 13 != 12)
            {
                photos.Add(assets[source.ImageFile]);
            }

            if (index % 10 == 0)
            {
                photos.Add(assets[catalog.Products[(index + 1) % catalog.Products.Length].ImageFile]);
            }

            foreach (var (asset, order) in photos.Distinct().Select((asset, order) => (asset, order)))
            {
                db.Add(new ProductImage { Id = Guid.NewGuid(), ProductId = product.Id, ImageAssetId = asset, IsPrimary = order == 0, SortOrder = order, CreatedAt = now, UpdatedAt = now });
            }

            if (index % 17 == 16 || index % 7 == 0)
            {
                var lot = await db.Set<InventoryLot>().SingleAsync(x => x.ProductId == product.Id, token);
                var commands = new List<InventoryCommand>();
                if (index % 17 == 16)
                {
                    commands.Add(new UpdateLot(lot.Id.ToString("D"), 0, OwnershipPatch.Empty));
                }
                else
                {
                    commands.Add(new ExtractItems(lot.Id.ToString("D"), ["display-item"], new(new(true, "opened"), new(true, "リビング / 飾り棚"), default, default)));
                    commands.Add(new UpdateItem("display-item", new(default, default, default, new(true, "展示用の個体。台座裏にサンプル番号のメモ。"))));
                    if (quantity >= 3)
                    {
                        commands.Add(new SplitLot(lot.Id.ToString("D"), 1, "spare", new(new(true, "unopened"), new(true, "書斎 / 予備ボックス"), default, default)));
                    }
                }

                var refs = await inventory.ApplyAsync(product, commands, now, token);
                if (refs.TryGetValue("display-item", out var item))
                {
                    db.Add(new InventoryItemImage { Id = Guid.NewGuid(), InventoryItemId = item, ImageAssetId = assets[source.ImageFile], SortOrder = 0, CreatedAt = now, UpdatedAt = now });
                }
            }

            if (index >= 62)
            {
                product.DeletedAt = now;
                await products.TouchAsync(product, now, token);
            }

            db.Add(new ProductChange { Id = Guid.NewGuid(), ProductId = product.Id, Revision = product.Revision, Kind = "created", OccurredAt = now });
            ids.Add(product.Id);
            await db.SaveChangesAsync(token);
        }

        await projection.RebuildAsync(ids.ToArray(), token);
        space.SampleStatus = "ready";
        space.Revision = checked(space.Revision + 1);
        space.UpdatedAt = now;
        await db.SaveChangesAsync(token);
    }

    /// <summary>専用コレクションだけに、架空のマスターとカテゴリ階層を構成する。</summary>
    private Dictionary<string, Dictionary<string, Guid>> CreateMasters(SampleCatalog catalog, DateTimeOffset now)
    {
        Dictionary<string, Guid> Named<T>(IEnumerable<string> names) where T : class, INamedMaster, new()
        {
            var values = names.Distinct().ToDictionary(name => name, _ => Guid.NewGuid());
            foreach (var (name, id) in values)
            {
                db.Add(new T { Id = id, Name = name, NormalizedName = MasterText.Normalize(name), CreatedAt = now, UpdatedAt = now });
            }

            return values;
        }

        var masters = new Dictionary<string, Dictionary<string, Guid>>
        {
            ["works"] = Named<Work>(catalog.MasterData["作品"]),
            ["series"] = Named<Series>(catalog.MasterData["シリーズ"]),
            ["characters"] = Named<Character>(catalog.MasterData["キャラクター"]),
            ["tags"] = Named<Tag>(catalog.MasterData["タグ"]),
            ["organizations"] = Named<Organization>(catalog.MasterData["組織"])
        };
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
        var names = parents.Values.Concat(catalog.Products.Select(x => x.Category)).Distinct().ToArray();
        masters["categories"] = Named<Category>(names);
        foreach (var category in db.Set<Category>().Local)
        {
            category.SortOrder = Array.IndexOf(names, category.Name);
            category.ParentCategoryId = parents.TryGetValue(category.Name, out var parent) ? masters["categories"][parent] : null;
        }

        foreach (var (work, series) in new[] { ("星巡りの旅", "星巡りシリーズ"), ("星巡りの旅（表記違い）", "星巡りシリーズ"), ("月と喫茶室", "喫茶室シリーズ") })
        {
            db.Add(new WorkSeries { WorkId = masters["works"][work], SeriesId = masters["series"][series] });
        }

        foreach (var (character, work) in new[] { ("蒼", "星巡りの旅"), ("リク", "星巡りの旅"), ("ルナ", "月と喫茶室"), ("ハル", "花のある日々") })
        {
            db.Add(new CharacterWork { CharacterId = masters["characters"][character], WorkId = masters["works"][work] });
        }

        return masters;
    }

    /// <summary>配布前に変換した固定画像を公開し、他コレクションと同じ内容のファイルは共有する。</summary>
    private async Task<Dictionary<string, Guid>> CreateImagesAsync(DateTimeOffset now, CancellationToken token)
    {
        using var manifest = SampleCatalog.Open("images.json");
        var images = await JsonSerializer.DeserializeAsync<SampleImage[]>(manifest, cancellationToken: token) ?? throw new InvalidDataException("サンプル画像定義がない");
        var policy = ImagePolicy.From(new ImageSettings());
        var assets = new Dictionary<string, Guid>();
        foreach (var image in images)
        {
            var stem = Path.GetFileNameWithoutExtension(image.File);
            var display = await PublishAsync(stem + ".display.webp", image.DisplayWidth, image.DisplayHeight, now, token);
            var thumbnail = await PublishAsync(stem + ".thumbnail.webp", image.ThumbnailWidth, image.ThumbnailHeight, now, token);
            var asset = new ImageAsset { Id = Guid.NewGuid(), SourceSha256 = image.SourceSha256, PolicyHash = policy.Hash, PolicyJson = policy.Json, DisplayFileId = display, ThumbnailFileId = thumbnail, CreatedAt = now };
            db.Add(asset);
            assets.Add(image.File, asset.Id);
        }

        await db.SaveChangesAsync(token);
        return assets;
    }

    /// <summary>先にファイルを確定する。DB確定前の中断で残ったファイルは、既存GCの猶予付き回収対象となる。</summary>
    private async Task<Guid> PublishAsync(string resource, int width, int height, DateTimeOffset now, CancellationToken token)
    {
        using var stream = SampleCatalog.Open(resource);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, token);
        var bytes = buffer.ToArray();
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var stored = db.Set<ImageFile>().Local.SingleOrDefault(x => x.Sha256 == hash) ?? await db.Set<ImageFile>().SingleOrDefaultAsync(x => x.Sha256 == hash, token);
        var staging = files.NewStagingPath();
        try
        {
            await File.WriteAllBytesAsync(files.Resolve(staging), bytes, token);
            await files.PublishAsync(files.Resolve(staging), hash, token);
        }
        finally
        {
            files.Delete(staging);
        }

        if (stored is null)
        {
            stored = new ImageFile { Id = Guid.NewGuid(), Sha256 = hash, RelativePath = ImageFiles.ObjectPath(hash), MediaType = "image/webp", Width = width, Height = height, ByteLength = bytes.Length, CreatedAt = now, UpdatedAt = now };
            db.Add(stored);
        }

        stored.State = "ready";
        stored.DeleteAfter = null;
        return stored.Id;
    }
}
