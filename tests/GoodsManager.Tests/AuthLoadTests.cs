using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
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

public sealed class AuthLoadTests
{
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

    [TestCase(65536, HttpStatusCode.Unauthorized)]
    [TestCase(65537, HttpStatusCode.RequestEntityTooLarge)]
    public async Task BodyBoundaryAllowsExactlyTheConfiguredNumberOfBytes(int length, HttpStatusCode expected)
    {
        await using var app = new TestApplication();
        using var client = Client(app);

        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        const string prefix = "{\"loginId\":\"unknown\",\"password\":\"x\",\"padding\":\"";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/password");
        request.Headers.Add("X-CSRF-Token", csrf.GetProperty("token").GetString());
        request.Content = new StringContent(prefix + new string('x', length - prefix.Length - 2) + "\"}", Encoding.UTF8, "application/json");

        Assert.That((await client.SendAsync(request)).StatusCode, Is.EqualTo(expected));
    }

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
        finally { gate.Semaphore.Release(); await Task.WhenAll(pending); }

        Assert.That(pending.Select(x => x.Result.StatusCode), Is.All.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await SendAsync(client, "auth/password", new
        {
            loginId = "unknown",
            password = Password
        })).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task OversizedBodyIsRejectedWithAndWithoutDeclaredLength(bool chunked)
    {
        await using var app = new TestApplication();
        using var client = Client(app);

        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/password");
        request.Headers.Add("X-CSRF-Token", csrf.GetProperty("token").GetString());

        var bytes = Encoding.UTF8.GetBytes("{\"loginId\":\"unknown\",\"password\":\"x\",\"padding\":\"" + new string('x', 65536) + "\"}");
        request.Content = chunked ? new StreamContent(new Unseekable(new MemoryStream(bytes))) : new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        Assert.That((await client.SendAsync(request)).StatusCode, Is.EqualTo(HttpStatusCode.RequestEntityTooLarge));
    }

    [TestCase("account/password")]
    [TestCase("auth/revoke-all")]
    [TestCase("unknown")]
    public async Task HashingDoesNotHoldWriteGateAndRevokedGenerationCannotIssueTicket(string operation)
    {
        var hasher = new PausingHasher();
        await using var app = new TestApplication { ConfigureTestServices = services => { services.RemoveAll<IPasswordHasher<AppUser>>(); services.AddSingleton<IPasswordHasher<AppUser>>(hasher); } };
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
        finally { hasher.Resume.Set(); }

        Assert.That((await pending).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        await using var scope = app.Services.CreateAsyncScope();

        Assert.That(await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuthTickets.CountAsync(), Is.EqualTo(operation == "unknown" ? 1 : 0));
    }

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

    private sealed class PausingHasher : IPasswordHasher<AppUser>
    {
        private readonly PasswordHasher<AppUser> inner = new(); private int armed;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Resume { get; } = new();

        public void Arm() => armed = 1;

        public string HashPassword(AppUser user, string password) => inner.HashPassword(user, password);

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

    private sealed class Unseekable(Stream inner) : Stream
    {
        public override bool CanRead => true; public override bool CanWrite => false; public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException(); set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
