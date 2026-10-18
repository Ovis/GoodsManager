using System.Text;
using System.Text.Json;
using GoodsManager.Web.Storage;

namespace GoodsManager.Web.Masters;

public sealed class MasterProblem(int status, string code, string? field = null) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public string? Field { get; } = field;
}

/// <summary>表示値の補正はせず、検索値だけ合意した順序で正規化</summary>
public static class MasterText
{
    public static string Normalize(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var output = new StringBuilder();
        var space = false;
        foreach (var rune in normalized.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                space = output.Length > 0;
            }
            else
            {
                if (space) output.Append(' ');
                output.Append(rune);
                space = false;
            }
        }
        return output.ToString();
    }
}

public sealed record MasterInput(string Name, string? WebsiteUrl, string? Memo, Guid? ParentId, int SortOrder)
{
    public static MasterInput Parse(string kind, JsonElement json, bool update)
    {
        try
        {
            var fields = kind switch
            {
                "works" or "series" or "characters" => new[] { "name" },
                "organizations" => ["name", "websiteUrl", "memo"],
                "categories" => ["name", "parentId", "sortOrder"],
                "tags" => ["name"],
                _ => throw new MasterProblem(404, "NOT_FOUND")
            };
            if (json.ValueKind != JsonValueKind.Object) throw Invalid();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in json.EnumerateObject())
            {
                if (!fields.Contains(property.Name) || !seen.Add(property.Name)) throw Invalid();
            }
            if (!seen.Contains("name") || (update && fields.Any(x => !seen.Contains(x)))) throw Invalid();
            if (json.GetProperty("name").ValueKind != JsonValueKind.String) throw Invalid("name");
            var name = json.GetProperty("name").GetString();
            if (!StorageValues.IsValidText(name, 1, 200) || string.IsNullOrWhiteSpace(name)) throw Invalid("name");
            var url = OptionalText(json, "websiteUrl", 2048);
            if (url is not null && (!(url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host)
                || url.Contains('\\') || url.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))) throw Invalid("websiteUrl");
            var memo = OptionalText(json, "memo", 4000);
            Guid? parent = json.TryGetProperty("parentId", out var parentJson) && parentJson.ValueKind != JsonValueKind.Null ? ReadId(parentJson) : null;
            var order = json.TryGetProperty("sortOrder", out var sortJson) ? sortJson.GetInt32() : 0;
            if (order < 0) throw Invalid("sortOrder");
            return new(name!, url, memo, parent, order);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or JsonException or OverflowException or ArgumentException)
        {
            throw Invalid();
        }
    }

    internal static Guid ReadId(JsonElement json)
    {
        try
        {
            return json.ValueKind == JsonValueKind.String && Guid.TryParseExact(json.GetString(), "D", out var id) && id != Guid.Empty ? id : throw Invalid();
        }
        catch (InvalidOperationException) { throw Invalid(); }
    }

    private static string? OptionalText(JsonElement json, string key, int max)
    {
        if (!json.TryGetProperty(key, out var item) || item.ValueKind == JsonValueKind.Null) return null;
        if (item.ValueKind != JsonValueKind.String) throw Invalid(key);
        var text = item.GetString();
        return StorageValues.IsValidText(text, 0, max) ? text : throw Invalid(key);
    }

    public static MasterProblem Invalid(string field = "body") => new(400, "VALIDATION_FAILED", field);
}
