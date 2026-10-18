using System.Security.Claims;
using GoodsManager.Web.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Auth;

/// <summary>標準チケットを暗号化して保持し、ログアウト後の古いクッキー再利用を防ぐ</summary>
public sealed class DatabaseTicketStore(IServiceScopeFactory scopes, IDataProtectionProvider protection, TimeProvider clock) : ITicketStore
{
    private readonly IDataProtector protector = protection.CreateProtector("GoodsManager.Identity.TicketStore.v1");

    /// <summary>有効期限を保持したチケットを保存</summary>
    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var id = Guid.NewGuid();
        await WriteAsync(id, ticket);
        return id.ToString("D");
    }

    /// <summary>標準ミドルウェアからの更新を保存。有効期限の自動延長は呼出側で禁止</summary>
    public Task RenewAsync(string key, AuthenticationTicket ticket) => WriteAsync(StorageValues.ParseId(key), ticket);

    /// <summary>削除済み・期限切れチケットを認証に使わない</summary>
    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        if (!Guid.TryParseExact(key, "D", out var id))
        {
            return null;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.AuthTickets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (row is null || row.ExpiresAt <= clock.GetUtcNow())
        {
            return null;
        }

        return TicketSerializer.Default.Deserialize(protector.Unprotect(row.ProtectedTicket));
    }

    /// <summary>現在のセッションをサーバー側から削除</summary>
    public async Task RemoveAsync(string key)
    {
        if (!Guid.TryParseExact(key, "D", out var id))
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuthTickets.Where(x => x.Id == id).ExecuteDeleteAsync();
    }

    /// <summary>期限切れを掃除し、個別チケットを保存</summary>
    private async Task WriteAsync(Guid id, AuthenticationTicket ticket)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.AuthTickets.Where(x => x.ExpiresAt <= clock.GetUtcNow()).ExecuteDeleteAsync();
        var row = await db.AuthTickets.FindAsync(id);
        if (row is null)
        {
            row = new AuthTicket { Id = id };
            db.AuthTickets.Add(row);
        }

        row.UserId = Guid.Parse(ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        row.ExpiresAt = ticket.Properties.ExpiresUtc ?? throw new InvalidOperationException("認証期限の指定が必要");
        row.ProtectedTicket = protector.Protect(TicketSerializer.Default.Serialize(ticket));
        await db.SaveChangesAsync();
    }
}
