using GoodsManager.Web.Auth;
using GoodsManager.Web.Collections;
using GoodsManager.Web.Storage;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Samples;

/// <summary>初期登録とは別にサンプルを準備する。再起動時もDBの未完了状態から処理を再開する。</summary>
public sealed class SampleCollectionWorker(IServiceScopeFactory scopes, AuthWriteGate gate, TimeProvider clock, ILogger<SampleCollectionWorker> logger) : BackgroundService
{
    /// <summary>要求の接続寿命から独立し、準備待ちを1件ずつ処理する。</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var id = await db.Set<CollectionSpace>().Where(x => x.SampleStatus == "pending").OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(stoppingToken);
                if (id.HasValue)
                {
                    await ProcessAsync(id.Value, stoppingToken);
                }
                else
                {
                    await Task.Delay(500, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogError("サンプル待機処理失敗 Code={Code} Type={Type}", "SAMPLE_WORKER_FAILED", error.GetType().Name);
                await Task.Delay(1000, stoppingToken);
            }
        }
    }

    /// <summary>商品・所持・マスター・検索投影を一括確定する。失敗してもアカウントや他コレクションは変更しない。</summary>
    public async Task ProcessAsync(Guid id, CancellationToken token)
    {
        await gate.Semaphore.WaitAsync(token);
        try
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await using var transaction = await db.Database.BeginTransactionAsync(token);
                var space = await db.Set<CollectionSpace>().SingleOrDefaultAsync(x => x.Id == id, token);
                if (space?.SampleStatus != "pending")
                {
                    return;
                }

                await scope.ServiceProvider.GetRequiredService<SampleCollectionSeeder>().SeedAsync(space, clock.GetUtcNow(), token);
                await transaction.CommitAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // 正常停止では失敗扱いにせず、次回起動で未完了の準備をやり直す。
                throw;
            }
            catch (Exception error)
            {
                logger.LogError("サンプル準備失敗 CollectionId={CollectionId} Code={Code} Type={Type}", id, "SAMPLE_CREATION_FAILED", error.GetType().Name);
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var space = await db.Set<CollectionSpace>().SingleOrDefaultAsync(x => x.Id == id, token);
                if (space?.SampleStatus == "pending")
                {
                    space.SampleStatus = "failed";
                    space.Revision = checked(space.Revision + 1);
                    space.UpdatedAt = clock.GetUtcNow();
                    var state = await db.SystemStates.SingleAsync(token);
                    state.DataRevision = checked(state.DataRevision + 1);
                    state.UpdatedAt = space.UpdatedAt;
                    await db.SaveChangesAsync(token);
                }
            }
        }
        finally
        {
            gate.Semaphore.Release();
        }
    }
}
