using System.Net.Http.Json;
using System.Text.Json;
using GoodsManager.Web.Collections;
using GoodsManager.Web.Masters;

namespace GoodsManager.Tests;

public sealed partial class MasterTests
{
    private const string CategoryOrderPrefix = "順序試験";
    private static Guid CategoryOrderId(int value) => Guid.Parse($"cafe0000-0000-4000-8000-{value:000000000000}");

    private async Task<Guid[]> SeedCategoryOrder()
    {
        Category Category(int id, string name, int order, int? parent = null) => new()
        {
            Id = CategoryOrderId(id), Name = name, NormalizedName = MasterText.Normalize(name),
            SortOrder = order, ParentCategoryId = parent.HasValue ? CategoryOrderId(parent.Value) : null,
            CreatedAt = app.Clock.GetUtcNow(), UpdatedAt = app.Clock.GetUtcNow()
        };
        var categories = new List<Category>
        {
            Category(100, "順序試験 A親", 20),
            Category(200, "順序試験 Z親", 10),
            Category(101, "順序試験 その他", 0, 100),
            Category(102, "順序試験 A子", 1, 100),
            Category(300, "順序試験 孫", 0, 201)
        };
        // 名前順・挿入順のどちらでも期待する階層順にはならないデータ。
        for (var i = 27; i >= 0; i--)
            categories.Add(Category(201 + i, i < 2 ? "順序試験 その他" : $"順序試験 子{27 - i:00}", Math.Max(0, i - 1), 200));
        await WithDb(async db => { db.AddRange(categories); await db.SaveChangesAsync(); });
        return new[] { 200, 201, 300 }.Concat(Enumerable.Range(202, 27)).Concat([100, 101, 102])
            .Select(CategoryOrderId).ToArray();
    }

    private static Guid[] CategoryPageIds(JsonElement page) => page.GetProperty("items")
        .EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToArray();

    [Test]
    public async Task CategoriesGroupWholeSubtreesBeforePaginationUsingConfiguredSiblingOrder()
    {
        var expected = await SeedCategoryOrder();
        var revision = await Revision();
        var first = await client.GetFromJsonAsync<JsonElement>($"/api/v1/masters/categories?q={CategoryOrderPrefix}&pageSize=25");
        var second = await client.GetFromJsonAsync<JsonElement>($"/api/v1/masters/categories?q={CategoryOrderPrefix}&pageSize=25&page=2");
        var all = await client.GetFromJsonAsync<JsonElement>($"/api/v1/masters/categories?q={CategoryOrderPrefix}&pageSize=50");
        Assert.Multiple(() =>
        {
            Assert.That(CategoryPageIds(first), Is.EqualTo(expected.Take(25)));
            Assert.That(CategoryPageIds(second), Is.EqualTo(expected.Skip(25)));
            Assert.That(CategoryPageIds(all), Is.EqualTo(expected));
            Assert.That(first.GetProperty("totalCount").GetInt64(), Is.EqualTo(33));
            Assert.That(first.GetProperty("totalPages").GetInt64(), Is.EqualTo(2));
            Assert.That(second.GetProperty("page").GetInt32(), Is.EqualTo(2));
            Assert.That(first.GetProperty("dataRevision").GetString(), Is.EqualTo(revision.ToString()));
        });
    }

    [Test]
    public async Task CategorySearchKeepsAncestorGroupsEvenWhenParentsDoNotMatch()
    {
        await SeedCategoryOrder();
        var result = await client.GetFromJsonAsync<JsonElement>("/api/v1/masters/categories?q=順序試験　その他&pageSize=25");
        var missing = await client.GetFromJsonAsync<JsonElement>("/api/v1/masters/categories?q=該当なし&pageSize=25");
        var overflow = await client.GetFromJsonAsync<JsonElement>($"/api/v1/masters/categories?q={CategoryOrderPrefix}&pageSize=25&page=2147483647");
        Assert.Multiple(() =>
        {
            Assert.That(CategoryPageIds(result), Is.EqualTo(new[] { 201, 202, 101 }.Select(CategoryOrderId)));
            Assert.That(result.GetProperty("totalCount").GetInt64(), Is.EqualTo(3));
            Assert.That(CategoryPageIds(missing), Is.Empty);
            Assert.That(missing.GetProperty("totalPages").GetInt64(), Is.Zero);
            Assert.That(CategoryPageIds(overflow), Is.Empty);
            Assert.That(overflow.GetProperty("totalCount").GetInt64(), Is.EqualTo(33));
        });
    }

    [Test]
    public async Task CategoryHierarchyListingDoesNotIncludeOtherCollections()
    {
        var expected = await SeedCategoryOrder();
        await WithDb(async db =>
        {
            var other = new CollectionSpace { Id = Guid.NewGuid(), Name = "別コレクション", CreatedAt = app.Clock.GetUtcNow(), UpdatedAt = app.Clock.GetUtcNow() };
            db.Add(other);
            var category = new Category { Id = Guid.NewGuid(), Name = CategoryOrderPrefix, NormalizedName = CategoryOrderPrefix,
                CreatedAt = app.Clock.GetUtcNow(), UpdatedAt = app.Clock.GetUtcNow() };
            db.Add(category);
            db.Entry(category).Property("CollectionId").CurrentValue = other.Id;
            await db.SaveChangesAsync();
        });
        var result = await client.GetFromJsonAsync<JsonElement>($"/api/v1/masters/categories?q={CategoryOrderPrefix}&pageSize=50");
        Assert.That(CategoryPageIds(result), Is.EqualTo(expected));
    }
}
