using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Masters;

public sealed partial class MastersController
{
    private async Task<object> PageCategoriesAsync(string search, int page, int size)
    {
        var token = HttpContext.RequestAborted;
        // ツリーと同じ兄弟間の順序を使い、階層全体を並べてから検索・ページ分割する。
        var categories = await db.Set<Category>().AsNoTracking()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync(token);
        var children = categories.ToLookup(x => x.ParentCategoryId ?? Guid.Empty);
        var pending = new Stack<Category>(children[Guid.Empty].Reverse());
        var visited = new HashSet<Guid>();
        var matches = new List<Category>();
        while (pending.TryPop(out var category))
        {
            token.ThrowIfCancellationRequested();
            if (!visited.Add(category.Id)) throw new MasterProblem(409, "CATEGORY_CYCLE");
            if (category.NormalizedName.Contains(search, StringComparison.Ordinal)) matches.Add(category);
            foreach (var child in children[category.Id].Reverse()) pending.Push(child);
        }
        if (visited.Count != categories.Count) throw new MasterProblem(409, "CATEGORY_CYCLE");

        var offset = ((long)page - 1) * size;
        var selected = offset > int.MaxValue ? [] : matches.Skip((int)offset).Take(size);
        var items = new List<object>();
        foreach (var category in selected) items.Add(await Dto("categories", category));
        var state = await db.SystemStates.AsNoTracking().SingleAsync(token);
        return new { items, page, pageSize = size, totalCount = (long)matches.Count,
            totalPages = ((long)matches.Count + size - 1) / size, dataRevision = Number(state.DataRevision) };
    }
}
