using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoodsManager.Web.Auth;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Collections;

/// <summary>所属を削除する操作でも結果が消えないよう、変更とアカウント単位の再送記録を同時に確定する。</summary>
public sealed class CollectionWrites(AppDbContext db, SignInManager<AppUser> signIn, AuthWriteGate gate, TimeProvider clock)
{
    /// <summary>コレクション管理の入力と更新条件を照合し、認証・書込直列化・再送を共通処理する。</summary>
    public async Task<IActionResult> ExecuteAsync(HttpContext context, string operation, object input, int status,
        Func<AppUser, DateTimeOffset, CancellationToken, Task<CollectionSpace>> action)
    {
        if (!Guid.TryParseExact(context.Request.Headers["Idempotency-Key"], "D", out var key) || key == Guid.Empty)
        {
            throw MasterInput.Invalid("idempotencyKey");
        }

        var condition = context.Request.Headers.IfMatch.ToString();
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            input,
            condition
        }))));
        var token = context.RequestAborted;
        await gate.Semaphore.WaitAsync(token);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            var user = await AuthenticationState.GetCurrentUserAsync(db, signIn, context.User) ?? throw new MasterProblem(401, "SESSION_EXPIRED");
            var now = clock.GetUtcNow();
            var previous = await db.Set<CollectionRequest>().SingleOrDefaultAsync(x => x.UserId == user.Id && x.Operation == operation && x.Key == key, token);
            if (previous is not null && previous.ExpiresAt > now)
            {
                if (previous.RequestHash != hash)
                {
                    throw new MasterProblem(409, "IDEMPOTENCY_KEY_REUSED");
                }

                context.Response.Headers.ETag = previous.Etag;
                return new ContentResult { StatusCode = previous.StatusCode, ContentType = "application/json", Content = previous.ResponseJson };
            }

            await db.Set<CollectionRequest>().Where(x => x.ExpiresAt <= now).ExecuteDeleteAsync(token);
            if (previous is not null)
            {
                db.Entry(previous).State = EntityState.Detached;
            }

            var space = await action(user, now, token);
            var etag = Etag(space);
            var response = JsonSerializer.Serialize(View(space), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            db.Add(new CollectionRequest { UserId = user.Id, Operation = operation, Key = key, RequestHash = hash, StatusCode = status, ResponseJson = response, Etag = etag, ExpiresAt = now.AddDays(7) });
            var state = await db.SystemStates.SingleAsync(token);
            state.DataRevision = checked(state.DataRevision + 1);
            state.UpdatedAt = now;
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            context.Response.Headers.ETag = etag;

            return new ContentResult { StatusCode = status, ContentType = "application/json", Content = response };
        }
        finally
        {
            gate.Semaphore.Release();
        }
    }

    /// <summary>管理画面へ返す所属名と更新条件。内部のサンプル準備状態も再取得できる。</summary>
    public static object View(CollectionSpace space, string role = "manager") => new
    {
        space.Id,
        space.Name,
        role,
        revision = space.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
        etag = Etag(space),
        space.SampleStatus
    };

    /// <summary>名前変更・サンプル準備状態の変化を検出する更新条件。</summary>
    public static string Etag(CollectionSpace space) => $"\"collection:{space.Id:D}:{space.Revision}\"";

    /// <summary>破壊的操作の確認後に、別の内容へ変わったコレクションを操作しない。</summary>
    public static void CheckVersion(CollectionSpace space, string condition)
    {
        if (condition.Length == 0)
        {
            throw new MasterProblem(428, "PRECONDITION_REQUIRED");
        }

        if (condition != Etag(space))
        {
            throw new MasterProblem(412, "REVISION_MISMATCH");
        }
    }
}
