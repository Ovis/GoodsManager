using System.Net;
using GoodsManager.Web.Auth;
using GoodsManager.Web.Masters;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using static GoodsManager.Tests.AuthenticationTests;

namespace GoodsManager.Tests;

/// <summary>パスキー認証待ち領域の上限と、期限・用途・ブラウザへの束縛を検証する。</summary>
public sealed class PasskeyCapacityTests
{
    /// <summary>認証待ち領域が満杯なら、有効な待ち情報を残して新規開始を429で拒否する。</summary>
    [Test]
    public async Task AnonymousOptionsAreRejectedWith429WhenFull()
    {
        await using var app = new TestApplication();
        using var client = Client(app);
        var state = app.Services.GetRequiredService<PasskeyCeremonies>();
        for (var i = 0; i < PasskeyCeremonies.Capacity; i++)
        {
            state.Begin(new DefaultHttpContext(), "login", null);
        }

        Assert.That((await SendAsync(client, "auth/passkey/options", new
        {
            rememberMe = false
        })).StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        Assert.That(state.ActiveCount, Is.EqualTo(PasskeyCeremonies.Capacity));
    }

    /// <summary>新しい認証開始がなくても30秒間隔の回収処理が期限切れ情報を除く。</summary>
    [Test]
    public async Task WorkerPrunesWithoutAnyNewBeginRequest()
    {
        var clock = new TimerClock();
        var state = new PasskeyCeremonies(clock);
        state.Begin(new DefaultHttpContext(), "login", null);

        using var worker = new PasskeyCleanupWorker(state, clock);
        await worker.StartAsync(CancellationToken.None);
        await clock.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.That(clock.Period, Is.EqualTo(TimeSpan.FromSeconds(30)));

        clock.AdvanceAndTick();

        Assert.That(await Task.Run(() => SpinWait.SpinUntil(() => state.ActiveCount == 0, TimeSpan.FromSeconds(2))), Is.True);

        await worker.StopAsync(CancellationToken.None);
    }

    /// <summary>時刻とタイマー通知を試験から制御し、実時間を待たず期限回収を検証する。</summary>
    private sealed class TimerClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        private TimerCallback? callback;
        private object? state;

        /// <summary>回収Workerが要求したタイマー周期。</summary>
        public TimeSpan Period
        {
            get; private set;
        }

        /// <summary>Workerがタイマーを登録し、通知を受け取れるようになった時点を示す。</summary>
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>期限判定に使う試験用の現在時刻を返す。</summary>
        public override DateTimeOffset GetUtcNow() => now;

        /// <summary>Workerのコールバックを保持し、登録完了を試験へ通知する。</summary>
        public override ITimer CreateTimer(TimerCallback action, object? value, TimeSpan dueTime, TimeSpan period)
        {
            callback = action;
            state = value;
            Period = period;
            Started.TrySetResult();

            return new ManualTimer();
        }

        /// <summary>5分進めた時刻で通知し、保持情報を期限切れとして回収させる。</summary>
        public void AdvanceAndTick()
        {
            now += TimeSpan.FromMinutes(5);
            callback!(state);
        }

        /// <summary>実時間で発火せず、試験側の明示通知だけを使うタイマー。</summary>
        private sealed class ManualTimer : ITimer
        {
            /// <summary>実時間のスケジュールを持たず、周期変更の要求だけを受理する。</summary>
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            /// <summary>OSタイマーを所有しないため解放処理を持たない。</summary>
            public void Dispose()
            {
            }

            /// <summary>OSタイマーを所有しないため非同期の解放待機を行わない。</summary>
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>並行した開始要求でも上限を超えず、先に受理した認証情報を追い出さないことを確認する。</summary>
    [Test]
    public void ParallelStartsNeverExceedCapacityAndDoNotEvictLiveCeremonies()
    {
        var state = new PasskeyCeremonies(new TestClock());
        var original = new DefaultHttpContext();
        var user = new AppUser { Id = Guid.NewGuid(), SecurityStamp = "generation" };
        var id = state.Begin(original, "login", user);
        var rejected = 0;
        Parallel.For(0, 512, _ =>
        {
            try
            {
                state.Begin(new DefaultHttpContext(), "login", null);
            }
            catch (MasterProblem error)
            {
                Assert.That(error.Status, Is.EqualTo(429));
                Interlocked.Increment(ref rejected);
            }
        });

        Assert.That(state.ActiveCount, Is.EqualTo(PasskeyCeremonies.Capacity));
        Assert.That(rejected, Is.EqualTo(512 - PasskeyCeremonies.Capacity + 1));

        original.Request.Headers.Cookie = "__Host-GoodsManager-Ceremony=" + id.ToString("D");

        Assert.That(state.Consume(original, id, "login", user), Is.Not.Null);
        Assert.That(state.Consume(original, id, "login", user), Is.Null);
    }

    /// <summary>容量制限の追加後も、別ブラウザ・別用途・認証世代変更・期限切れを拒否する。</summary>
    [Test]
    public void ExpiryPurposeBrowserAndGenerationStillApplyAtCapacity()
    {
        var clock = new TestClock();
        var state = new PasskeyCeremonies(clock);
        var browser = new DefaultHttpContext();
        var user = new AppUser { Id = Guid.NewGuid(), SecurityStamp = "first" };
        var id = state.Begin(browser, "register", user);

        Assert.That(state.Consume(new DefaultHttpContext(), id, "register", user), Is.Null);

        browser.Request.Headers.Cookie = "__Host-GoodsManager-Ceremony=" + id.ToString("D");
        user.SecurityStamp = "next";

        Assert.That(state.Consume(browser, id, "register", user), Is.Null);

        id = state.Begin(browser, "login", user);
        browser.Request.Headers.Cookie = "__Host-GoodsManager-Ceremony=" + id.ToString("D");

        Assert.That(state.Consume(browser, id, "register", user), Is.Null);

        state.Begin(new DefaultHttpContext(), "login", user);
        clock.Advance(TimeSpan.FromMinutes(5));
        state.PruneExpired();

        Assert.That(state.ActiveCount, Is.Zero);
        Assert.DoesNotThrow(() => state.Begin(new DefaultHttpContext(), "login", user));
    }
}
