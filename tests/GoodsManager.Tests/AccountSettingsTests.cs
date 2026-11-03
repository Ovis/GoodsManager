using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoodsManager.Web.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using static GoodsManager.Tests.AuthenticationTests;

namespace GoodsManager.Tests;

/// <summary>設定変更と認証失効の対象範囲、および古い版への変更拒否を検証する。</summary>
public sealed class AccountSettingsTests
{
    /// <summary>全端末失効とパスワード変更が本人のセッションだけを削除し、別ユーザーを巻き込まないことを確認する。</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task RevocationOnlyEndsTheActingUsersSessions(bool changePassword)
    {
        await using var app = new TestApplication();
        using var owner = Client(app);
        await SetupAsync(owner);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var user = new AppUser { Id = Guid.NewGuid(), UserName = "another", LockoutEnabled = true };

            Assert.That((await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().CreateAsync(user, Password)).Succeeded, Is.True);
        }

        using var other = Client(app);
        using var secondOwner = Client(app);

        Assert.That((await SendAsync(other, "auth/password", new
        {
            loginId = "another",
            password = Password
        })).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        await SendAsync(secondOwner, "auth/password", new
        {
            loginId = "owner",
            password = Password
        });

        var response = changePassword
            ? await SendAsync(owner, "account/password", new
            {
                newPassword = "changed-password-123",
                verification = new
                {
                    password = Password
                }
            })
            : await SendAsync(owner, "auth/revoke-all");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await owner.GetAsync("/api/v1/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await secondOwner.GetAsync("/api/v1/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        var retained = await other.GetFromJsonAsync<JsonElement>("/api/v1/auth/session");

        Assert.That(retained.GetProperty("user").GetProperty("loginId").GetString(), Is.EqualTo("another"));

        using var fresh = Client(app);

        Assert.That((await SendAsync(fresh, "auth/password", new
        {
            loginId = "owner",
            password = changePassword ? "changed-password-123" : Password
        })).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    /// <summary>ID変更でログイン状態を保ち、古いETagによる上書きを拒否する。</summary>
    [Test]
    public async Task LoginIdChangeKeepsSessionsAndPasswordAndRejectsOldEtag()
    {
        await using var app = new TestApplication();
        using var first = Client(app);
        await SetupAsync(first);

        using var second = Client(app);
        await SendAsync(second, "auth/password", new
        {
            loginId = "owner",
            password = Password
        });

        var original = await first.GetFromJsonAsync<JsonElement>("/api/v1/auth/session");
        var etag = original.GetProperty("user").GetProperty("etag").GetString();

        Assert.That((await SendAsync(first, "account", new
        {
            loginId = "new.owner"
        }, HttpMethod.Put, etag)).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        foreach (var client in new[] { first, second })
        {
            var session = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/session");

            Assert.That(session.GetProperty("user").GetProperty("loginId").GetString(), Is.EqualTo("new.owner"));
        }

        Assert.That((await SendAsync(first, "account", new
        {
            loginId = "stale"
        }, HttpMethod.Put, etag)).StatusCode, Is.EqualTo(HttpStatusCode.PreconditionFailed));

        using var next = Client(app);

        Assert.That((await SendAsync(next, "auth/password", new
        {
            loginId = "new.owner",
            password = Password
        })).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
