using GoodsManager.Web.Masters;
using Microsoft.AspNetCore.WebUtilities;

namespace GoodsManager.Web.Auth;

/// <summary>標準Identityの一時状態に、サーバー側の単回使用と用途の照合だけを追加</summary>
public sealed class PasskeyCeremonies(TimeProvider clock)
{
    private const string CookieName = "__Host-GoodsManager-Ceremony";
    private readonly object gate = new();
    private readonly Dictionary<Guid, Ceremony> pending = new();

    /// <summary>全ユーザーと匿名ブラウザを合わせて保持できる認証待ち情報の上限。</summary>
    public const int Capacity = 128;

    /// <summary>認証待ち情報の保持数。期限切れの項目は回収されるまで含む。</summary>
    public int ActiveCount
    {
        get
        {
            lock (gate)
            {
                return pending.Count;
            }
        }
    }

    /// <summary>開始要求がなくても期限切れの情報を解放する。</summary>
    public void PruneExpired()
    {
        lock (gate)
        {
            foreach (var id in pending.Where(x => x.Value.ExpiresAt <= clock.GetUtcNow()).Select(x => x.Key).ToArray())
            {
                pending.Remove(id);
            }
        }
    }

    /// <summary>ブラウザの前回の処理を破棄し、5分間だけ保持</summary>
    public Guid Begin(HttpContext context, string purpose, AppUser? user, bool rememberMe = false, IReadOnlyDictionary<Guid, string?>? userStamps = null)
    {
        lock (gate)
        {
            PruneExpired();

            var hasPrevious = Guid.TryParse(context.Request.Cookies[CookieName], out var previous) && pending.ContainsKey(previous);
            if (pending.Count >= Capacity && !hasPrevious)
            {
                throw new MasterProblem(429, "PASSKEY_CAPACITY_REACHED");
            }

            if (hasPrevious)
            {
                pending.Remove(previous);
            }

            var id = Guid.NewGuid();
            var expires = clock.GetUtcNow().AddMinutes(5);
            pending.Add(id, new Ceremony(purpose, user?.Id, user?.SecurityStamp, expires, rememberMe, userStamps));
            context.Response.Cookies.Append(CookieName, id.ToString("D"), new CookieOptions { Secure = true, HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/", MaxAge = TimeSpan.FromMinutes(5) });

            return id;
        }
    }

    /// <summary>成功・失敗にかかわらず消費し、別用途・別ユーザー・失効後の処理を拒否</summary>
    public Ceremony? Consume(HttpContext context, Guid id, string purpose, AppUser? user = null)
    {
        if (context.Request.Cookies[CookieName] != id.ToString("D"))
        {
            return null;
        }

        context.Response.Cookies.Delete(CookieName, new CookieOptions { Secure = true, Path = "/" });

        Ceremony? ceremony;
        lock (gate)
        {
            pending.Remove(id, out ceremony);
        }

        if (ceremony is null || ceremony.ExpiresAt <= clock.GetUtcNow() || ceremony.Purpose != purpose)
        {
            return null;
        }

        if (user is not null && (user.Id != ceremony.UserId || user.SecurityStamp != ceremony.SecurityStamp))
        {
            return null;
        }

        return ceremony;
    }

    /// <summary>バイト列で定義された認証IDを、標準WebAuthnのURL向けBase64でAPIへ返す</summary>
    public static string CredentialId(byte[] id) => WebEncoders.Base64UrlEncode(id);
}

/// <summary>暗号検証の状態はIdentityが保持し、この記録には重複保存しない</summary>
public sealed record Ceremony(string Purpose, Guid? UserId, string? SecurityStamp, DateTimeOffset ExpiresAt, bool RememberMe, IReadOnlyDictionary<Guid, string?>? UserStamps = null);
