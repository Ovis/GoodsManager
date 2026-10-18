using System.Diagnostics.CodeAnalysis;
using System.Text;
using GoodsManager.Web.Storage;

namespace GoodsManager.Web.Products;

/// <summary>表示用の入力表記、種類ごとの検証済み値、種類をまたぐ14桁比較値。</summary>
public sealed record IdentifierValue(string Scheme, string RawValue, string NormalizedValue, string ComparisonValue);

/// <summary>コードを数値へ変換せず、許可した文字だけを正規化する。割当の実在確認は行わない。</summary>
public static class IdentifierCodes
{
    public static bool TryNormalize(string? scheme, string? raw, [NotNullWhen(true)] out IdentifierValue? result)
    {
        result = null;
        var length = scheme switch { "ean8" => 8, "upca" => 12, "ean13" or "isbn13" => 13, "isbn10" => 10, _ => 0 };
        if (length == 0 || !StorageValues.IsValidText(raw, 1, int.MaxValue)) return false;
        var text = new StringBuilder(length);
        foreach (var original in raw!)
        {
            if (original is ' ' or '　' or '-') continue;
            var value = original is >= '０' and <= '９' ? (char)('0' + original - '０') : original;
            if (scheme == "isbn10" && text.Length == 9 && value == 'x') value = 'X';
            if (!(value is >= '0' and <= '9') && !(scheme == "isbn10" && text.Length == 9 && value == 'X')) return false;
            if (text.Length == length) return false;
            text.Append(value);
        }
        if (text.Length != length) return false;
        var normalized = text.ToString();
        string comparable;
        if (scheme == "isbn10")
        {
            var sum = 0;
            for (var i = 0; i < normalized.Length; i++) sum += (10 - i) * (normalized[i] == 'X' ? 10 : normalized[i] - '0');
            if (sum % 11 != 0) return false;
            var prefix = "978" + normalized[..9];
            comparable = prefix + CheckDigit(prefix);
        }
        else
        {
            if (scheme == "isbn13" && !(normalized.StartsWith("978", StringComparison.Ordinal) || normalized.StartsWith("979", StringComparison.Ordinal))) return false;
            if (CheckDigit(normalized.AsSpan(0, length - 1)) != normalized[^1]) return false;
            comparable = normalized;
        }
        result = new IdentifierValue(scheme!, raw!, normalized, comparable.PadLeft(14, '0'));
        return true;
    }

    private static char CheckDigit(ReadOnlySpan<char> prefix)
    {
        var sum = 0;
        var weight = 3;
        for (var i = prefix.Length - 1; i >= 0; i--)
        {
            sum += (prefix[i] - '0') * weight;
            weight = 4 - weight;
        }
        return (char)('0' + (10 - sum % 10) % 10);
    }
}
