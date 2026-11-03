using System.Security.Claims;

namespace GoodsManager.Web.Auth;

/// <summary>入力や秘密を引数に持たない固定の認証監査イベント。</summary>
public enum AuthEvent
{
    AUTH_SETUP_CREATED = 2200, AUTH_SETUP_REJECTED,
    AUTH_PASSWORD_LOGIN = 2210, AUTH_PASSWORD_FAILED, AUTH_PASSWORD_LOCKED, AUTH_PASSWORD_VERIFICATION_FAILED,
    AUTH_PASSKEY_LOGIN = 2220, AUTH_PASSKEY_FAILED,
    AUTH_PASSWORD_CHANGED = 2230, AUTH_LOGIN_ID_CHANGED, AUTH_CHANGE_REJECTED, AUTH_ACCOUNT_RECOVERED,
    AUTH_PASSKEY_ADDED = 2240, AUTH_PASSKEY_REMOVED, AUTH_PASSKEY_RENAMED,
    AUTH_ALL_SESSIONS_REVOKED = 2250, AUTH_LOGOUT,
    AUTH_RATE_LIMITED = 2260, AUTH_REQUEST_REJECTED
}

/// <summary>失敗は先頭1件と60秒ごとの残件数を記録し、ユーザー別の集計も有限にする。</summary>
public sealed class AuthAudit(ILogger<AuthAudit> logger, TimeProvider clock)
{
    private readonly object gate = new();
    private readonly Dictionary<(AuthEvent Event, Guid? User), long> pending = new();
    private readonly Dictionary<AuthEvent, long> overflow = new();
    private DateTimeOffset windowStart = clock.GetUtcNow();

    /// <summary>内部ユーザーIDを含む集計キーの全体上限。超過分はIDを残さずイベント別に合算する。</summary>
    public const int MaximumGroups = 128;

    /// <summary>保留中のユーザー別集計と、上限超過分のイベント別集計の総数。</summary>
    public int GroupCount
    {
        get
        {
            lock (gate)
            {
                return pending.Count + overflow.Count;
            }
        }
    }

    /// <summary>認証済みPrincipalから内部ユーザーIDだけを取り出す。生のログインIDは扱わない。</summary>
    public static Guid? UserId(ClaimsPrincipal user) => Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    /// <summary>成功と重要な変更は即時記録し、反復する失敗は件数を失わず集計する。</summary>
    public void Record(AuthEvent kind, Guid? userId = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (kind is not (AuthEvent.AUTH_SETUP_REJECTED or AuthEvent.AUTH_PASSWORD_FAILED or AuthEvent.AUTH_PASSWORD_VERIFICATION_FAILED or AuthEvent.AUTH_PASSKEY_FAILED
            or AuthEvent.AUTH_CHANGE_REJECTED or AuthEvent.AUTH_RATE_LIMITED or AuthEvent.AUTH_REQUEST_REJECTED))
        {
            Write(kind, userId, 1, false, clock.GetUtcNow(), clock.GetUtcNow());
            return;
        }

        bool first;
        DateTimeOffset start;
        lock (gate)
        {
            var key = (kind, userId);
            start = windowStart;
            if (pending.TryGetValue(key, out var count))
            {
                pending[key] = count + 1;
                first = false;
            }
            else if (pending.Count < MaximumGroups)
            {
                pending.Add(key, 0);
                first = true;
            }
            else
            {
                userId = null;
                first = !overflow.TryGetValue(kind, out count);
                overflow[kind] = first ? 0 : count + 1;
            }
        }

        if (first)
        {
            Write(kind, userId, 1, false, start, clock.GetUtcNow());
        }
    }

    /// <summary>終了時も残件数を出力する。上限超過分はIDを保持せずイベント別に合算する。</summary>
    public void Flush()
    {
        List<(AuthEvent Kind, Guid? User, long Count)> entries;
        DateTimeOffset start;
        var end = clock.GetUtcNow();
        lock (gate)
        {
            entries = pending.Where(x => x.Value > 0).Select(x => (x.Key.Event, x.Key.User, x.Value))
                .Concat(overflow.Where(x => x.Value > 0).Select(x => (x.Key, (Guid?)null, x.Value))).ToList();
            start = windowStart;
            pending.Clear();
            overflow.Clear();
            windowStart = end;
        }

        foreach (var entry in entries)
        {
            Write(entry.Kind, entry.User, entry.Count, true, start, end);
        }
    }

    /// <summary>固定イベントと型を限定した照合情報だけをログへ渡す。</summary>
    private void Write(AuthEvent kind, Guid? user, long count, bool aggregated, DateTimeOffset start, DateTimeOffset end) =>
        logger.LogInformation(new EventId((int)kind), "認証監査 Code={Code} UserId={UserId} Count={Count} Aggregated={Aggregated} WindowStart={WindowStart} WindowEnd={WindowEnd}", kind.ToString(), user, count, aggregated, start, end);

    /// <summary>同じ要求の拒否を操作側とミドルウェア側で重複計上しない。</summary>
    public void Failure(HttpContext context, AuthEvent kind, Guid? user = null)
    {
        if (context.Items.ContainsKey(typeof(AuthAudit)))
        {
            return;
        }

        context.Items[typeof(AuthAudit)] = true;
        Record(kind, user ?? UserId(context.User));
    }

    /// <summary>MVCの自動検証・CSRF・認可・本文上限も、自由入力を残さず拒否として監査する。</summary>
    public static async Task ObserveAsync(HttpContext context, RequestDelegate next)
    {
        if (AuthTraffic.Group(context) == "none")
        {
            await next(context);
            return;
        }

        var audit = context.RequestServices.GetRequiredService<AuthAudit>();
        try
        {
            await next(context);
        }
        catch
        {
            audit.Failure(context, AuthEvent.AUTH_REQUEST_REJECTED);
            throw;
        }
        finally
        {
            if (context.Response.StatusCode >= 400)
            {
                audit.Failure(context, AuthEvent.AUTH_REQUEST_REJECTED);
            }
        }
    }
}

/// <summary>認証失敗の保留件数を定期的に出力し、正常終了時にも残件を記録する。</summary>
public sealed class AuthAuditWorker(AuthAudit audit, TimeProvider clock) : BackgroundService
{
    /// <summary>60秒ごとに集計窓を区切り、反復する認証失敗の件数を出力する。</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            audit.Flush();
        }
    }

    /// <summary>定期処理を停止した後、終了時点の未出力件数を必ず記録する。</summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await base.StopAsync(cancellationToken);
        }
        finally
        {
            audit.Flush();
        }
    }
}
