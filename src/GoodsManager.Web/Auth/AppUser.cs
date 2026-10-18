using Microsoft.AspNetCore.Identity;

namespace GoodsManager.Web.Auth;

/// <summary>標準Identityユーザー。1件制約だけを追加</summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public int Singleton { get; set; } = 1;
}

/// <summary>標準クッキーミドルウェアのITicketStore用保存領域</summary>
public sealed class AuthTicket
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public byte[] ProtectedTicket { get; set; } = [];
    public DateTimeOffset ExpiresAt { get; set; }
}
