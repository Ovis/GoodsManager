using System.Net;
using GoodsManager.Web.Auth;
using GoodsManager.Web.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using static GoodsManager.Tests.AuthenticationTests;

namespace GoodsManager.Tests;

/// <summary>認証負荷の全体制限と、並行する認証・失効の順序を検証する。</summary>
public sealed class AuthLoadTests
{
    /// <summary>匿名クライアントを作り直しても重い認証要求の頻度制限を回避できないことを確認する。</summary>
    [Test]
    public async Task HeavyFrequencyIsAlsoGlobalAcrossAnonymousClients()
    {
        await using var app = new TestApplication { ConfigureTestServices = services => services.Configure<AuthTrafficOptions>(x => x.HeavyPerMinute = 2) };
        for (var i = 0; i < 3; i++)
        {
            using var client = Client(app);

            Assert.That((await SendAsync(client, "auth/password", new
            {
                loginId = "unknown" + i,
                password = Password
            })).StatusCode,
                Is.EqualTo(i < 2 ? HttpStatusCode.Unauthorized : HttpStatusCode.TooManyRequests));
        }
    }

    /// <summary>送信元を偽装するヘッダーで読み取り制限を分割できず、生存確認は継続することを確認する。</summary>
    [Test]
    public async Task GlobalReadLimitIgnoresSuppliedPeerHeadersAndLeavesLiveAvailable()
    {
        await using var app = new TestApplication { ConfigureTestServices = services => services.Configure<AuthTrafficOptions>(x => x.ReadPerMinute = 2) };
        using var client = Client(app);
        for (var i = 0; i < 3; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/csrf");
            request.Headers.Add("X-Forwarded-For", "192.0.2." + (i + 1));
            request.Headers.Host = "different" + i + ".invalid";

            var response = await client.SendAsync(request);

            Assert.That(response.StatusCode, Is.EqualTo(i < 2 ? HttpStatusCode.OK : HttpStatusCode.TooManyRequests));

            if (i == 2)
            {
                Assert.That(response.Headers.RetryAfter, Is.Not.Null);
            }
        }

        Assert.That((await client.GetAsync("/health/live")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    /// <summary>実行枠と待機枠の上限を守り、完了後には後続要求を受け付けることを確認する。</summary>
    [Test]
    public async Task HeavyConcurrencyAndQueueAreBoundedAndReleased()
    {
        await using var app = new TestApplication();
        using var client = Client(app);
        var gate = app.Services.GetRequiredService<AuthWriteGate>();
        await gate.Semaphore.WaitAsync();

        var limiter = app.Services.GetRequiredService<IOptions<RateLimiterOptions>>().Value.GlobalLimiter!;
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/auth/password";
        context.Request.Method = "POST";

        Task<HttpResponseMessage>[] pending = [];
        try
        {
            pending = Enumerable.Range(0, 4).Select(_ => SendAsync(client, "auth/password", new { loginId = "unknown", password = Password })).ToArray();

            Assert.That(await Task.Run(() => SpinWait.SpinUntil(() => limiter.GetStatistics(context)?.CurrentQueuedCount == 2, TimeSpan.FromSeconds(5))), Is.True);
            Assert.That((await SendAsync(client, "auth/password", new
            {
                loginId = "unknown",
                password = Password
            })).StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        }
        finally
        {
            gate.Semaphore.Release();
            await Task.WhenAll(pending);
        }

        Assert.That(pending.Select(x => x.Result.StatusCode), Is.All.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await SendAsync(client, "auth/password", new
        {
            loginId = "unknown",
            password = Password
        })).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    /// <summary>ハッシュ検証中に設定変更でき、検証途中で失効した世代にチケットを発行しないことを確認する。</summary>
    [TestCase("account/password")]
    [TestCase("auth/revoke-all")]
    [TestCase("unknown")]
    public async Task HashingDoesNotHoldWriteGateAndRevokedGenerationCannotIssueTicket(string operation)
    {
        var hasher = new PausingHasher();
        await using var app = new TestApplication
        {
            ConfigureTestServices = services =>
            {
                services.RemoveAll<IPasswordHasher<AppUser>>();
                services.AddSingleton<IPasswordHasher<AppUser>>(hasher);
            }
        };
        using var owner = Client(app);
        await SetupAsync(owner);

        using var login = Client(app);
        hasher.Arm();

        var pending = SendAsync(login, "auth/password", new
        {
            loginId = operation == "unknown" ? "unknown" : "owner",
            password = Password
        });
        try
        {
            await hasher.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var gate = app.Services.GetRequiredService<AuthWriteGate>();

            var entered = await gate.Semaphore.WaitAsync(TimeSpan.FromSeconds(1));

            Assert.That(entered, Is.True);

            if (entered)
            {
                gate.Semaphore.Release();
            }

            if (operation != "unknown")
            {
                var changed = await SendAsync(owner, operation, new
                {
                    newPassword = "new-isolated-password",
                    verification = new
                    {
                        password = Password
                    }
                });

                Assert.That(changed.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            }
        }
        finally
        {
            hasher.Resume.Set();
        }

        Assert.That((await pending).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        await using var scope = app.Services.CreateAsyncScope();

        Assert.That(await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuthTickets.CountAsync(), Is.EqualTo(operation == "unknown" ? 1 : 0));
    }

    /// <summary>並行して受理した失敗を取りこぼさず、5回でパスワードをロックする。</summary>
    [Test]
    public async Task AcceptedParallelFailuresAreCountedAndLockAtFive()
    {
        await using var app = new TestApplication();
        using var owner = Client(app);
        await SetupAsync(owner);

        using var client = Client(app);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => SendAsync(client, "auth/password", new { loginId = "owner", password = "incorrect" })));

        Assert.That(results.Select(x => x.StatusCode), Is.All.EqualTo(HttpStatusCode.Unauthorized));

        await SendAsync(client, "auth/password", new
        {
            loginId = "owner",
            password = "incorrect"
        });

        Assert.That((await SendAsync(client, "auth/password", new
        {
            loginId = "owner",
            password = Password
        })).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        Assert.That(await users.IsLockedOutAsync((await users.FindByNameAsync("owner"))!), Is.True);
    }

    /// <summary>標準ハッシュ検証の終了直後を停止し、書き込みゲートとの競合を再現する。</summary>
    private sealed class PausingHasher : IPasswordHasher<AppUser>
    {
        private readonly PasswordHasher<AppUser> inner = new();
        private int armed;

        /// <summary>検証結果が得られた時点を試験側へ通知する。</summary>
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>競合操作を完了した試験側から検証処理を再開する。</summary>
        public ManualResetEventSlim Resume { get; } = new();

        /// <summary>次のハッシュ検証1回だけを停止対象にする。</summary>
        public void Arm() => armed = 1;

        /// <summary>試験でもIdentity標準のハッシュ形式を維持する。</summary>
        public string HashPassword(AppUser user, string password) => inner.HashPassword(user, password);

        /// <summary>標準検証の結果を保持したまま、試験用の再開通知まで待機する。</summary>
        public PasswordVerificationResult VerifyHashedPassword(AppUser user, string hashedPassword, string providedPassword)
        {
            var result = inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
            if (Interlocked.Exchange(ref armed, 0) == 1)
            {
                Entered.TrySetResult();
                if (!Resume.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException();
                }
            }

            return result;
        }
    }

}
