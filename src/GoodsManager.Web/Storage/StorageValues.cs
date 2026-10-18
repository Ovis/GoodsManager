using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GoodsManager.Web.Storage;

/// <summary>DB境界で使用する保存表現</summary>
public static class StorageValues
{
    public const long MinimumTimestamp = -62135596800000;
    public const long MaximumTimestamp = 253402300799999;
    public const long MaximumSafeInteger = 9007199254740991;

    /// <summary>UUIDを小文字のD形式へ揃える</summary>
    public static string FormatId(Guid value) => value.ToString("D");

    /// <summary>非正規の保存表現を拒否してUUIDを復元</summary>
    public static Guid ParseId(string value) =>
        Guid.TryParseExact(value, "D", out var id) && FormatId(id) == value
            ? id : throw new FormatException("UUIDの保存形式が不正");

    /// <summary>UTCのUnixミリ秒へ変換。原点前の端数も標準変換に従う</summary>
    public static long ToMilliseconds(DateTimeOffset value) => value.ToUnixTimeMilliseconds();

    /// <summary>UTCの日時へ復元し、対応範囲外を拒否</summary>
    public static DateTimeOffset FromMilliseconds(long value) => DateTimeOffset.FromUnixTimeMilliseconds(value);

    /// <summary>APIへ出力できる数量合計かを検査</summary>
    public static long RequireSafeTotal(long value) => value is >= 0 and <= MaximumSafeInteger
        ? value : throw new OverflowException("数量合計が安全な整数の範囲外");

    /// <summary>破損データを暗黙のfalseへ置き換えず拒否</summary>
    public static bool ParseBoolean(long value) => value switch
    {
        0 => false,
        1 => true,
        _ => throw new InvalidDataException("真偽値の保存値が不正")
    };

    /// <summary>補助文字を含めコードポイント数を数え、不正なUTF-16とNULも拒否</summary>
    public static bool IsValidText(string? value, int minimum, int maximum)
    {
        if (value is null || value.Contains('\0'))
        {
            return false;
        }

        var count = 0;
        var remaining = value.AsSpan();
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out _, out var consumed) != System.Buffers.OperationStatus.Done)
            {
                return false;
            }

            count++;
            remaining = remaining[consumed..];
        }

        return count >= minimum && count <= maximum;
    }

    /// <summary>共通の型変換と保存型CHECKを全対象列へ適用</summary>
    public static void ApplyConventions(ModelBuilder model)
    {
        foreach (var entity in model.Model.GetEntityTypes())
        {
            // Identity標準のパスキーメタデータはJSON所有型。物理列として二重定義しない
            if (entity.IsOwned())
            {
                continue;
            }

            foreach (var property in entity.GetProperties())
            {
                var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                var column = $"\"{property.Name}\"";
                string? rule = null;
                if (type == typeof(Guid))
                {
                    property.SetValueConverter(new ValueConverter<Guid, string>(v => FormatId(v), v => ParseId(v)));
                    property.SetColumnType("TEXT");
                    rule = $"typeof({column}) = 'text' AND length({column}) = 36 AND instr({column}, char(0)) = 0 AND substr({column},9,1) = '-' AND substr({column},14,1) = '-' AND substr({column},19,1) = '-' AND substr({column},24,1) = '-' AND length(replace({column},'-','')) = 32 AND replace({column},'-','') NOT GLOB '*[^0-9a-f]*'";
                }
                else if (type == typeof(DateTimeOffset))
                {
                    property.SetValueConverter(new ValueConverter<DateTimeOffset, long>(v => ToMilliseconds(v), v => FromMilliseconds(v)));
                    property.SetColumnType("INTEGER");
                    rule = $"typeof({column}) = 'integer' AND {column} BETWEEN {MinimumTimestamp} AND {MaximumTimestamp}";
                }
                else if (type == typeof(bool))
                {
                    property.SetValueConverter(new ValueConverter<bool, long>(v => v ? 1 : 0, v => ParseBoolean(v)));
                    property.SetColumnType("INTEGER");
                    rule = $"typeof({column}) = 'integer' AND {column} IN (0,1)";
                }
                else if (type == typeof(long) || type == typeof(int))
                {
                    rule = $"typeof({column}) = 'integer'";
                }
                else if (type == typeof(byte[]))
                {
                    rule = $"typeof({column}) = 'blob'";
                }
                else if (type == typeof(string))
                {
                    rule = $"typeof({column}) = 'text' AND instr({column}, char(0)) = 0";
                }

                if (rule is not null)
                {
                    if (property.IsNullable)
                    {
                        rule = $"{column} IS NULL OR ({rule})";
                    }

                    model.Entity(entity.ClrType).ToTable(table => table.HasCheckConstraint($"CK_{entity.GetTableName()}_{property.Name}_Storage", rule));
                }
            }
        }
    }
}
