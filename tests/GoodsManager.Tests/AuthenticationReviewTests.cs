using System.Net;
using GoodsManager.Web.Auth;
using GoodsManager.Web.Storage;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static GoodsManager.Tests.AuthenticationTests;

namespace GoodsManager.Tests;

/// <summary>失効と同時実行の境界で見つかった不具合の再現</summary>
public sealed class AuthenticationReviewTests
{
    /// <summary>認証済みの要求が待機している間に失効した場合、古い追跡情報を再利用しない</summary>
    [TestCase("auth/revoke-all", false)]
    [TestCase("auth/password", false)]
    [TestCase("auth/password", true)]
    public async Task PasswordChangeInvalidatesRequestsWaitingAfterCookieValidation(string path, bool useNewPassword)
    {
        var validated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var app = new TestApplication
        {
            ConfigureTestServices = services => services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options =>
            {
                var original = options.Events.OnValidatePrincipal;
                options.Events.OnValidatePrincipal = async context =>
                {
                    await original(context);
                    if (context.Request.Path == "/api/v1/" + path && context.Request.Headers.ContainsKey("X-Review-Pause"))
                    {
                        validated.TrySetResult();
                        await resume.Task.WaitAsync(TimeSpan.FromSeconds(15));
                    }
                };
            })
        };
        using var waiting = Client(app);
        await SetupAsync(waiting);
        using var changing = Client(app);
        await SendAsync(changing, "auth/password", new { loginId = "owner", password = Password, rememberMe = false });
        waiting.DefaultRequestHeaders.Add("X-Review-Pause", "true");
        var pending = SendAsync(waiting, path, new { loginId = "owner", password = useNewPassword ? "changed-password" : Password, rememberMe = false });
        try
        {
            await validated.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var changed = await SendAsync(changing, "account/password", new { newPassword = "changed-password", verification = new { password = Password } });
            Assert.That(changed.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        }
        finally
        {
            resume.TrySetResult();
        }

        Assert.That((await pending).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        await using var scope = app.Services.CreateAsyncScope();
        Assert.That(await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuthTickets.CountAsync(), Is.Zero);
    }

    /// <summary>短い誤パスワードも連続失敗として計数し、標準ロックアウトを適用</summary>
    [Test]
    public async Task ShortIncorrectPasswordsCountTowardsLockout()
    {
        await using var app = new TestApplication();
        using var client = Client(app);
        await SetupAsync(client);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.That((await SendAsync(client, "auth/password", new { loginId = "owner", password = "x", rememberMe = false })).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        Assert.That((await SendAsync(client, "auth/password", new { loginId = "owner", password = Password, rememberMe = false })).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    /// <summary>古い要求によるチケット更新で、削除・期限切れのセッションを復活させない</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task RenewCannotResurrectRevokedOrExpiredTicket(bool expire)
    {
        await using var app = new TestApplication();
        using var client = Client(app);
        await SetupAsync(client);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<DatabaseTicketStore>();
        var key = (await db.AuthTickets.AsNoTracking().SingleAsync()).Id.ToString("D");
        var ticket = (await store.RetrieveAsync(key))!;
        if (expire)
        {
            app.Clock.Advance(TimeSpan.FromDays(8));
        }
        else
        {
            await store.RemoveAsync(key);
        }

        ticket.Properties.ExpiresUtc = app.Clock.GetUtcNow().AddDays(7);
        Assert.ThrowsAsync<AuthenticationFailureException>(() => store.RenewAsync(key, ticket));
        Assert.That(await store.RetrieveAsync(key), Is.Null);
    }
}
