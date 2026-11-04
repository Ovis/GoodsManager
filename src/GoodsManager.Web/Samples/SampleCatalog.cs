using System.Text.Json;

namespace GoodsManager.Web.Samples;

/// <summary>モック用に自作した架空商品とイラスト。ネットワークや利用者のファイルからは取得しない。</summary>
public sealed record SampleCatalog(Dictionary<string, string[]> MasterData, SampleProduct[] Products)
{
    /// <summary>商品メモでも試用データと識別できる共通表記。</summary>
    public const string Marker = "動作確認用サンプル";

    /// <summary>本体に同梱した固定カタログだけを読み込む。</summary>
    public static SampleCatalog Load()
    {
        using var stream = Open("catalog.json");
        return JsonSerializer.Deserialize<SampleCatalog>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("同梱サンプルを読み込めない");
    }

    /// <summary>アセンブリ内の固定リソースを開く。パスやURLの入力は受け付けない。</summary>
    public static Stream Open(string file) => typeof(SampleCatalog).Assembly.GetManifestResourceStream("sample-data/" + file)
        ?? throw new InvalidDataException("同梱サンプル画像が見つからない");

    /// <summary>検索やバーコード入力を試せる、チェック桁付きの架空EAN-13。</summary>
    public static string Barcode(int id)
    {
        var digits = $"49012345{id:0000}";
        var sum = digits.Select((c, index) => (c - '0') * (index % 2 == 0 ? 1 : 3)).Sum();
        return digits + (10 - sum % 10) % 10;
    }
}

/// <summary>架空商品の名前・分類・関連と、共用イラストの参照。</summary>
public sealed record SampleProduct(int Id, string Name, string[] Works, string Category, string[] Characters, string Memo, string ImageFile, SampleLot[] Lots);

/// <summary>元のモックで設定した所持数量。</summary>
public sealed record SampleLot(int Quantity);

/// <summary>既存の画像変換処理で配布前に生成したWebPの寸法と、元PNGの識別値。</summary>
public sealed record SampleImage(string File, string SourceSha256, int DisplayWidth, int DisplayHeight, int ThumbnailWidth, int ThumbnailHeight);
