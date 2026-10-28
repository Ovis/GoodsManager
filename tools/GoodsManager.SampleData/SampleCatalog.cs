using System.Text.Json;

namespace GoodsManager.SampleData;

/// <summary>モック由来の架空データと画像。利用者の環境や認証情報は含めない。</summary>
public sealed record SampleCatalog(Dictionary<string, string[]> MasterData, SampleProduct[] Products)
{
    public const string Marker = "ローカル動作確認用サンプル";
    public const string Version = "mock-64-v1";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>実行ディレクトリやmockブランチに依存せず、同梱データを読む。</summary>
    public static SampleCatalog Load()
    {
        using var stream = OpenImage("catalog.json");
        return JsonSerializer.Deserialize<SampleCatalog>(stream, Json) ?? throw new InvalidDataException("サンプルが読み込めません。");
    }

    /// <summary>同梱リソースだけを開き、任意のローカルファイルを送信しない。</summary>
    public static Stream OpenImage(string file) => typeof(SampleCatalog).Assembly.GetManifestResourceStream("sample-data/" + file)
        ?? throw new InvalidDataException("同梱ファイルがありません: " + file);

    public static string ProductMarker(int id) => $"【{Marker} #{id:000}】";

    /// <summary>元のモックの仮コードを、チェック桁付きのEAN-13にする。</summary>
    public static string Barcode(int id)
    {
        var digits = $"49012345{id:0000}";
        var sum = digits.Select((c, index) => (c - '0') * (index % 2 == 0 ? 1 : 3)).Sum();
        return digits + (10 - sum % 10) % 10;
    }
}

public sealed record SampleProduct(int Id, string Name, string[] Works, string Category, string[] Characters, string Memo, string ImageFile, SampleLot[] Lots);
public sealed record SampleLot(int Quantity);
