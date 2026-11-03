using System.Net;
using GoodsManager.Web.Auth;
using GoodsManager.Web.Masters;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using static GoodsManager.Tests.AuthenticationTests;

namespace GoodsManager.Tests;

public sealed class PasskeyCapacityTests
{
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

    private sealed class TimerClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        private TimerCallback? callback; private object? state;
        public TimeSpan Period
        {
            get; private set;
        }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override DateTimeOffset GetUtcNow() => now;

        public override ITimer CreateTimer(TimerCallback action, object? value, TimeSpan dueTime, TimeSpan period)
        {
            callback = action;
            state = value;
            Period = period;
            Started.TrySetResult();

            return new ManualTimer();
        }

        public void AdvanceAndTick()
        {
            now += TimeSpan.FromMinutes(5);
            callback!(state);
        }

        private sealed class ManualTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

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
            catch (MasterProblem error) { Assert.That(error.Status, Is.EqualTo(429)); Interlocked.Increment(ref rejected); }
        });

        Assert.That(state.ActiveCount, Is.EqualTo(PasskeyCeremonies.Capacity));
        Assert.That(rejected, Is.EqualTo(512 - PasskeyCeremonies.Capacity + 1));

        original.Request.Headers.Cookie = "__Host-GoodsManager-Ceremony=" + id.ToString("D");

        Assert.That(state.Consume(original, id, "login", user), Is.Not.Null);
        Assert.That(state.Consume(original, id, "login", user), Is.Null);
    }

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
