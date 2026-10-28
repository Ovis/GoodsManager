using Microsoft.AspNetCore.Identity;

namespace GoodsManager.Web.Auth;

/// <summary>標準Identityユーザーと、アプリ全体の管理権限。</summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public bool IsAdministrator { get; set; }
}

/// <summary>標準クッキーミドルウェアのITicketStore用保存領域</summary>
public sealed class AuthTicket
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public byte[] ProtectedTicket { get; set; } = [];
    public DateTimeOffset ExpiresAt { get; set; }
}
