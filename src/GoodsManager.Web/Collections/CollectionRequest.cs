namespace GoodsManager.Web.Collections;

/// <summary>コレクション削除後も再送結果を返すため、業務データの所属から独立して保持する変更記録。</summary>
public sealed class CollectionRequest
{
    /// <summary>再送結果を参照できるアカウント。</summary>
    public Guid UserId
    {
        get; set;
    }

    /// <summary>要求先と操作を区別する固定の識別値。</summary>
    public string Operation { get; set; } = "";

    /// <summary>同じ要求を識別するクライアント発行キー。</summary>
    public Guid Key
    {
        get; set;
    }

    /// <summary>本文と更新条件の取り違えを拒否するダイジェスト。</summary>
    public string RequestHash { get; set; } = "";

    /// <summary>確定済みのHTTP応答コード。</summary>
    public int StatusCode
    {
        get; set;
    }

    /// <summary>確定時の結果。削除済みコレクションの情報も再取得できる。</summary>
    public string ResponseJson { get; set; } = "";

    /// <summary>確定時の更新条件。現在の内容へ自動的に置き換えない。</summary>
    public string Etag { get; set; } = "";

    /// <summary>同じキーの結果を照合できる期限。</summary>
    public DateTimeOffset ExpiresAt
    {
        get; set;
    }
}
