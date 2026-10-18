using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Search;
using GoodsManager.Web.Storage;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Products;

/// <summary>表示表記を残す商品入力とは分け、重複判定に使う値だけを正規化</summary>
public sealed record DuplicateInput(string Name, Guid[] WorkIds, IdentifierInput[] Identifiers, Guid? CategoryId, Guid[] CharacterIds)
{
    /// <summary>商品属性から重複判定に必要な入力だけを取り出す</summary>
    public static DuplicateInput From(ProductDraft draft) => new(draft.Name, draft.WorkIds, draft.Identifiers, draft.CategoryId, draft.CharacterIds);
    /// <summary>事前確認の入力を検証し、IDの順序と重複を正規化</summary>
    public static DuplicateInput Parse(JsonElement body)
    {
        var x = new InputObject(body, "name", "workIds", "identifiers", "categoryId", "characterIds");
        Guid[] Ids(string field) => RegistrationLimits.Array(x.Array(field), field).Select(v => v.ValueKind == JsonValueKind.String && Guid.TryParseExact(v.GetString(), "D", out var id) && id != Guid.Empty ? id : throw InputObject.Invalid(field)).Distinct().Order().ToArray();
        return new(x.NullableText("name", 200, trim: false) ?? "", Ids("workIds"), RegistrationLimits.Array(x.Array("identifiers"), "identifiers").Select(v => { var i = new InputObject(v, "scheme", "value"); return new IdentifierInput(i.Text("scheme"), i.Text("value")); }).ToArray(), x.OptionalId("categoryId"), x.Has("characterIds") ? Ids("characterIds") : []);
    }
    public string NormalizedName => MasterText.Normalize(Name);
    /// <summary>有効な識別コードを14桁の比較値へ揃える</summary>
    public string[] ComparisonCodes()
    {
        return Identifiers.Select(x => StorageValues.IsValidText(x.Value, 1, 200) && IdentifierCodes.TryNormalize(x.Scheme, x.Value, out var value) ? value.ComparisonValue : throw InputObject.Invalid("identifiers")).Distinct().Order(StringComparer.Ordinal).ToArray();
    }
    /// <summary>正規化した重複判定入力を順序に依存しないハッシュへ変換</summary>
    public string Fingerprint()
    {
        var json = JsonSerializer.Serialize(new { name = NormalizedName, workIds = WorkIds.Distinct().Order().Select(StorageValues.FormatId), codes = ComparisonCodes(), categoryId = CategoryId?.ToString("D"), characterIds = CharacterIds.Distinct().Order().Select(StorageValues.FormatId) });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
}
public sealed record ConfirmedCandidate(Guid ProductId, string[] Reasons);
public sealed record DuplicateConfirmation(string InputFingerprint, ConfirmedCandidate[] ConfirmedCandidates, string Decision, Guid? TargetProductId)
{
    /// <summary>確認データは認証の代替に使わず、最新の候補集合と理由を後で照合</summary>
    public static DuplicateConfirmation? Parse(InputObject input)
    {
        if (!input.Has("duplicateConfirmation") || input.Get("duplicateConfirmation").ValueKind == JsonValueKind.Null) return null;
        var x = new InputObject(input.Get("duplicateConfirmation"), "inputFingerprint", "confirmedCandidates", "decision", "targetProductId");
        var candidates = x.Array("confirmedCandidates").Select(v =>
        {
            var c = new InputObject(v, "productId", "reasons");
            var reasons = c.Array("reasons").Select(r => r.ValueKind == JsonValueKind.String && r.GetString() is "CODE_EXACT" or "NAME_WORK_EXACT" ? r.GetString()! : throw InputObject.Invalid("reasons")).Distinct().ToArray();
            if (reasons.Length == 0) throw InputObject.Invalid("reasons");
            return new ConfirmedCandidate(c.Id("productId"), reasons);
        }).ToArray();
        if (candidates.DistinctBy(c => c.ProductId).Count() != candidates.Length) throw InputObject.Invalid("confirmedCandidates");
        var fingerprint = x.Text("inputFingerprint", 64); var decision = x.Text("decision"); var target = x.OptionalId("targetProductId");
        if (fingerprint.Length != 64 || fingerprint.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))) throw InputObject.Invalid("inputFingerprint");
        if (decision is not ("createSeparate" or "addToExisting") || (decision == "createSeparate" && target.HasValue) || (decision == "addToExisting" && !target.HasValue)) throw InputObject.Invalid("decision");
        return new(fingerprint, candidates, decision, target);
    }
}
public sealed record DuplicateCandidate(ProductSummary Product, string Strength, string[] Reasons, bool IsTrashed);
public sealed record DuplicateResult(string InputFingerprint, DuplicateCandidate[] Candidates, int StrongCandidateCount, int WeakCandidateCount);

/// <summary>照会と保存で共通の判定。保存側は書込ゲートと同一トランザクション内から呼ぶ</summary>
public sealed class DuplicateChecks(AppDbContext db, ProductSearch search)
{
    /// <summary>全強候補を取得し、所持0やゴミ箱を候補から除外しない</summary>
    public async Task<DuplicateResult> CheckAsync(DuplicateInput input, Guid? excludeProductId, CancellationToken token)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("重複確認はスナップショット内で実行する");
        if (!StorageValues.IsValidText(input.Name, 0, 200)) throw InputObject.Invalid("name");
        var fingerprint = input.Fingerprint(); var name = input.NormalizedName; var codes = input.ComparisonCodes(); var works = input.WorkIds;
        var characters = input.CharacterIds;
        var query = db.Set<Product>().AsNoTracking().Where(p => p.Id != excludeProductId).Select(p => new
        {
            p.Id,
            p.DeletedAt,
            p.CreatedAt,
            Code = db.Set<ProductIdentifier>().Any(c => c.ProductId == p.Id && codes.Contains(c.ComparisonValue)),
            NameWork = name != "" && p.NormalizedName == name && db.Set<ProductWork>().Any(w => w.ProductId == p.Id && works.Contains(w.WorkId)),
            NameExact = name != "" && p.NormalizedName == name,
            NamePartial = name != "" && p.NormalizedName != "" && (p.NormalizedName.Contains(name) || name.Contains(p.NormalizedName)),
            CategoryCharacter = input.CategoryId.HasValue && p.CategoryId == input.CategoryId && db.Set<ProductCharacter>().Any(c => c.ProductId == p.Id && characters.Contains(c.CharacterId))
        });
        var strong = await query.Where(h => h.Code || h.NameWork).OrderByDescending(h => h.CreatedAt).ThenBy(h => h.Id).ToArrayAsync(token);
        var weakQuery = query.Where(h => !h.Code && !h.NameWork && (h.NameExact || h.NamePartial || h.CategoryCharacter));
        var weakCount = await weakQuery.CountAsync(token);
        var weak = await weakQuery.OrderBy(h => h.NameExact ? 0 : h.NamePartial ? 1 : 2).ThenByDescending(h => h.CreatedAt).ThenBy(h => h.Id).Take(20).ToArrayAsync(token);
        var hits = strong.Concat(weak).ToArray();
        var summaries = (await search.ByIdsAsync(hits.Select(x => x.Id).ToArray(), token)).ToDictionary(x => x.Id);
        var candidates = hits.Select(h => new DuplicateCandidate(summaries[h.Id], h.Code || h.NameWork ? "strong" : "weak",
            h.Code || h.NameWork ? (h.Code ? new[] { "CODE_EXACT" } : []).Concat(h.NameWork ? new[] { "NAME_WORK_EXACT" } : []).ToArray()
                : h.NameExact ? ["NAME_EXACT"] : h.NamePartial ? ["NAME_PARTIAL"] : ["CATEGORY_CHARACTER"], h.DeletedAt.HasValue)).ToArray();
        return new(fingerprint, candidates, strong.Length, weakCount);
    }
    /// <summary>入力が同じで、最新の強候補・理由が確認済み集合の部分集合なら再確認を省く</summary>
    public async Task RequireConfirmationAsync(DuplicateInput input, DuplicateConfirmation? confirmation, string decision, Guid? target, CancellationToken token)
    {
        var latest = await CheckAsync(input, null, token);
        var valid = confirmation is not null && confirmation.InputFingerprint == latest.InputFingerprint && confirmation.Decision == decision && confirmation.TargetProductId == target;
        if (valid) valid = latest.Candidates.Where(x => x.Strength == "strong").All(candidate => confirmation!.ConfirmedCandidates.Any(c => c.ProductId == candidate.Product.Id && candidate.Reasons.All(r => c.Reasons.Contains(r, StringComparer.Ordinal))));
        if ((!valid && latest.StrongCandidateCount > 0) || (decision == "addToExisting" && !valid)) throw new MasterProblem(409, "DUPLICATE_CONFIRMATION_REQUIRED") { Details = latest };
    }
}
