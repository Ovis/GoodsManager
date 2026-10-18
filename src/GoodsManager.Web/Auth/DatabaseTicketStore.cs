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
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.AuthTickets.Where(x => x.ExpiresAt <= clock.GetUtcNow()).ExecuteDeleteAsync();
        db.AuthTickets.Add(new AuthTicket
        {
            Id = id,
            UserId = Guid.Parse(ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier)!),
            ExpiresAt = ticket.Properties.ExpiresUtc ?? throw new InvalidOperationException("認証期限の指定が必要"),
            ProtectedTicket = protector.Protect(TicketSerializer.Default.Serialize(ticket))
        });
        await db.SaveChangesAsync();
        return id.ToString("D");
    }

    /// <summary>有効な既存チケットだけを更新し、削除済み・期限切れのセッションを復活させない</summary>
    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        var id = StorageValues.ParseId(key);
        var userId = Guid.Parse(ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var expires = ticket.Properties.ExpiresUtc ?? throw new InvalidOperationException("認証期限の指定が必要");
        var bytes = protector.Protect(TicketSerializer.Default.Serialize(ticket));
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // 読取後の失効との競合も、条件付きUPDATEの1文で判定する
        var updated = await db.AuthTickets.Where(x => x.Id == id && x.UserId == userId && x.ExpiresAt > clock.GetUtcNow())
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ExpiresAt, expires).SetProperty(x => x.ProtectedTicket, bytes));
        if (updated != 1)
        {
            throw new AuthenticationFailureException("認証セッションが失効");
        }
    }

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

}
