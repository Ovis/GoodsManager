namespace GoodsManager.Web.Products;

/// <summary>完全削除後も残す商品変更記録。正本への外部キーは張らず、同期機能は持たない</summary>
public sealed class ProductChange
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public long Revision { get; set; }
    public string Kind { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
}
