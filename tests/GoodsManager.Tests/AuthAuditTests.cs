using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoodsManager.Web.Auth;
using GoodsManager.Web.Operations;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static GoodsManager.Tests.AuthenticationTests;

namespace GoodsManager.Tests;

/// <summary>認証監査の分類・件数・障害時の出力と、秘密を保存しない制約を検証する。</summary>
public sealed class AuthAuditTests
{
    /// <summary>MVCが同じ操作へ解決するURLは、表記によらず同じ失敗イベントに集計する。</summary>
    [TestCase("auth/password", "AUTH_PASSWORD_FAILED", HttpStatusCode.Unauthorized)]
    [TestCase("auth/password/", "AUTH_PASSWORD_FAILED", HttpStatusCode.Unauthorized)]
    [TestCase("AUTH/PASSWORD", "AUTH_PASSWORD_FAILED", HttpStatusCode.Unauthorized)]
    [TestCase("setup", "AUTH_SETUP_REJECTED", HttpStatusCode.Forbidden)]
    [TestCase("setup/", "AUTH_SETUP_REJECTED", HttpStatusCode.Forbidden)]
    [TestCase("SETUP", "AUTH_SETUP_REJECTED", HttpStatusCode.Forbidden)]
    public async Task AuditClassificationUsesTheActionInsteadOfTheRawPath(string path, string expectedEvent, HttpStatusCode status)
    {
        await using var app = new TestApplication();
        using var client = Client(app);
        var response = await SendAsync(client, path, new
        {
            loginId = "unknown",
            password = Password,
            setupToken = "invalid"
        });
        Assert.That(response.StatusCode, Is.EqualTo(status));
        app.Services.GetRequiredService<AuthAudit>().Flush();

        var events = Records(ReadLogs(app)).Where(x => x.TryGetProperty("errorCode", out var code)
            && code.GetString() is "AUTH_PASSWORD_FAILED" or "AUTH_SETUP_REJECTED" or "AUTH_CHANGE_REJECTED" or "AUTH_REQUEST_REJECTED").ToArray();
        Assert.That(events.Select(x => x.GetProperty("errorCode").GetString()), Is.EqualTo(new[] { expectedEvent }));
        Assert.That(events.Single().GetProperty("Count").GetInt64(), Is.EqualTo(1));
    }

    /// <summary>本人確認の失敗を操作間で数え、ロックへの遷移だけを一度記録する。</summary>
    [TestCase("password")]
    [TestCase("passkey-add")]
    [TestCase("passkey-delete")]
    public async Task OperationPasswordFailuresAuditOneLockTransition(string operation)
    {
        await using var app = new TestApplication();
        using var owner = Client(app);
        await SetupAsync(owner);
        using var authenticator = new TestAuthenticator();
        var enrollment = await (await SendAsync(owner, "account/passkeys/options", new
        {
            verification = new
            {
                password = Password
            }
        }))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.That((await SendAsync(owner, "account/passkeys", new
        {
            challengeId = enrollment.GetProperty("challengeId"),
            credential = authenticator.Create(enrollment.GetProperty("publicKeyOptions")),
            displayName = "本人確認の試験用"
        })).StatusCode, Is.EqualTo(HttpStatusCode.Created));

        for (var i = 0; i < 7; i++)
        {
            var verification = new
            {
                password = "private-incorrect-verification"
            };
            var response = operation switch
            {
                "password" => await SendAsync(owner, "account/password", new { newPassword = "private-new-password", verification }),
                "passkey-add" => await SendAsync(owner, "account/passkeys/options", new { verification }),
                _ => await SendAsync(owner, "account/passkeys/" + authenticator.Id, verification, HttpMethod.Delete, await PasskeyTests.EtagAsync(owner, authenticator.Id))
            };
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        }

        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await users.FindByNameAsync("owner") ?? throw new InvalidOperationException("試験ユーザーが存在しない");
        Assert.That(await users.IsLockedOutAsync(user), Is.True);
        app.Services.GetRequiredService<AuthAudit>().Flush();

        var text = ReadLogs(app);
        var events = Records(text).Where(x => x.TryGetProperty("errorCode", out _)).ToArray();
        Assert.That(events.Where(x => x.GetProperty("errorCode").GetString() == "AUTH_PASSWORD_VERIFICATION_FAILED")
            .Sum(x => x.GetProperty("Count").GetInt64()), Is.EqualTo(7));
        Assert.That(events.Count(x => x.GetProperty("errorCode").GetString() == "AUTH_PASSWORD_LOCKED"), Is.EqualTo(1));
        Assert.That(events.Where(x => x.GetProperty("errorCode").GetString() == "AUTH_PASSWORD_LOCKED")
            .Select(x => x.GetProperty("UserId").GetGuid()), Is.All.EqualTo(user.Id));
        Assert.That(text, Does.Not.Contain("AUTH_CHANGE_REJECTED").And.Not.Contain("AUTH_PASSKEY_FAILED")
            .And.Not.Contain("AUTH_REQUEST_REJECTED").And.Not.Contain("private-incorrect-verification").And.Not.Contain("private-new-password"));
    }

    /// <summary>ファイル出力が壊れても重要な認証変更と障害通知を標準出力へ残す。</summary>
    [Test]
    public async Task CriticalAuditFallsBackToConsoleWhenFileStorageFails()
    {
        var directory = Path.Combine(Path.GetTempPath(), "GoodsManager.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "audit-file-failure");
        var clock = new TestClock();
        using var output = new StringWriter();
        using var logs = new OperationalLogging(path, clock, output);
        using var factory = new LoggerFactory([logs]);

        try
        {
            Directory.Delete(path);
            await File.WriteAllTextAsync(path, "blocked");

            var audit = new AuthAudit(factory.CreateLogger<AuthAudit>(), clock);
            var user = Guid.NewGuid();

            Assert.DoesNotThrow(() => audit.Record(AuthEvent.AUTH_PASSWORD_CHANGED, user));
            Assert.That(logs.FileFailed, Is.True);
            Assert.That(output.ToString(), Does.Contain("AUTH_PASSWORD_CHANGED").And.Contain("LOG_STORAGE_FAILED").And.Contain(user.ToString()));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>アカウント操作を内部IDで追跡でき、入力値やWebAuthnデータがログに残らないことを確認する。</summary>
    [Test]
    public async Task AccountEventsAreTraceableWithoutPasswordsTokensNamesOrCredentials()
    {
        await using var app = new TestApplication();
        using var owner = Client(app);
        var token = File.ReadLines(Path.Combine(app.DataDirectory, "setup-token.txt")).First();
        await SetupAsync(owner);

        var session = await owner.GetFromJsonAsync<JsonElement>("/api/v1/auth/session");
        var id = session.GetProperty("user").GetProperty("id").GetGuid();
        using var anonymous = Client(app);
        await SendAsync(anonymous, "auth/password", new
        {
            loginId = "private.unknown.login",
            password = "private-incorrect-password"
        });
        await SendAsync(anonymous, "auth/password", new
        {
            loginId = "owner",
            password = "private-incorrect-password"
        });
        await SendAsync(owner, "auth/password", new
        {
            loginId = "owner",
            password = Password
        });

        using var authenticator = new TestAuthenticator();

        var registration = await (await SendAsync(owner, "account/passkeys/options", new
        {
            verification = new
            {
                password = Password
            }
        })).Content.ReadFromJsonAsync<JsonElement>();
        var credential = authenticator.Create(registration.GetProperty("publicKeyOptions"));

        Assert.That((await SendAsync(owner, "account/passkeys", new
        {
            challengeId = registration.GetProperty("challengeId"),
            credential,
            displayName = "private-passkey-name"
        })).StatusCode, Is.EqualTo(HttpStatusCode.Created));

        await SendAsync(owner, "account/passkeys/" + authenticator.Id, new
        {
            displayName = "private-renamed-key"
        }, HttpMethod.Put, await PasskeyTests.EtagAsync(owner, authenticator.Id));

        var options = await (await SendAsync(anonymous, "auth/passkey/options", new
        {
            loginId = "owner",
            rememberMe = false
        })).Content.ReadFromJsonAsync<JsonElement>();
        var signed = authenticator.Get(options.GetProperty("publicKeyOptions"));

        Assert.That((await SendAsync(anonymous, "auth/passkey/verify", new
        {
            challengeId = options.GetProperty("challengeId"),
            credential = signed
        })).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        await SendAsync(anonymous, "auth/passkey/verify", new
        {
            challengeId = options.GetProperty("challengeId"),
            credential = signed
        });

        Assert.That((await SendAsync(owner, "account/passkeys/" + authenticator.Id, new
        {
            password = Password
        }, HttpMethod.Delete, await PasskeyTests.EtagAsync(owner, authenticator.Id))).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        session = await owner.GetFromJsonAsync<JsonElement>("/api/v1/auth/session");
        await SendAsync(owner, "account", new
        {
            loginId = "private.changed.login"
        }, HttpMethod.Put, session.GetProperty("user").GetProperty("etag").GetString());

        Assert.That((await SendAsync(owner, "account/password", new
        {
            newPassword = "private-new-password",
            verification = new
            {
                password = Password
            }
        })).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        using var next = Client(app);
        await SendAsync(next, "auth/password", new
        {
            loginId = "private.changed.login",
            password = "private-new-password"
        });

        Assert.That((await SendAsync(next, "auth/revoke-all")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        app.Services.GetRequiredService<AuthAudit>().Flush();

        var text = ReadLogs(app);
        var records = Records(text);
        var codes = records.Where(x => x.TryGetProperty("errorCode", out _)).Select(x => x.GetProperty("errorCode").GetString()).ToArray();
        foreach (var expected in new[] { "AUTH_SETUP_CREATED", "AUTH_PASSWORD_LOGIN", "AUTH_PASSWORD_FAILED", "AUTH_PASSKEY_ADDED", "AUTH_PASSKEY_RENAMED", "AUTH_PASSKEY_REMOVED", "AUTH_PASSKEY_LOGIN", "AUTH_PASSKEY_FAILED", "AUTH_LOGIN_ID_CHANGED", "AUTH_PASSWORD_CHANGED", "AUTH_ALL_SESSIONS_REVOKED" })
        {
            Assert.That(codes, Does.Contain(expected));
        }

        foreach (var secret in new[] { token, Password, "private-incorrect-password", "private-new-password", "private.unknown.login", "private.changed.login", "private-passkey-name", "private-renamed-key", authenticator.Id, credential.GetRawText(), signed.GetRawText() })
        {
            Assert.That(text.Contains(secret, StringComparison.Ordinal), Is.False, "認証監査に入力や認証データを含めない");
        }

        foreach (var record in records.Where(x => x.TryGetProperty("errorCode", out var code) && code.GetString() == "AUTH_PASSWORD_CHANGED"))
        {
            Assert.That(record.GetProperty("UserId").GetGuid(), Is.EqualTo(id));
        }

        var failures = records.Where(x => x.TryGetProperty("errorCode", out var code) && code.GetString() == "AUTH_PASSWORD_FAILED").ToArray();

        Assert.That(failures.Any(x => x.TryGetProperty("UserId", out var user) && user.GetGuid() == id), Is.True);
    }

    /// <summary>パスワードロックとCSRF拒否を、それぞれ固定イベントで記録する。</summary>
    [Test]
    public async Task PasswordLockAndRequestRejectionHaveFixedAuditEvents()
    {
        await using var app = new TestApplication();
        using var owner = Client(app);
        await SetupAsync(owner);

        using var client = Client(app);
        for (var i = 0; i < 5; i++)
        {
            await SendAsync(client, "auth/password", new
            {
                loginId = "owner",
                password = "wrong"
            });
        }

        await client.PostAsJsonAsync("/api/v1/setup", new
        {
            loginId = "private.body",
            password = "private-body-password"
        });

        var text = ReadLogs(app);

        Assert.That(text, Does.Contain("AUTH_PASSWORD_LOCKED").And.Contain("AUTH_REQUEST_REJECTED"));
        Assert.That(text, Does.Not.Contain("private.body").And.Not.Contain("private-body-password"));
    }

    /// <summary>多数のユーザーへの失敗でも集計領域を有限にし、終了時まで総件数を保持する。</summary>
    [Test]
    public async Task AggregationBoundsGroupsPreservesCountsAndFlushesOnShutdown()
    {
        var directory = Path.Combine(Path.GetTempPath(), "GoodsManager.Tests", Guid.NewGuid().ToString("N"));
        var clock = new TestClock();
        using var output = new StringWriter();
        using var logs = new OperationalLogging(directory, clock, output);
        using var factory = new LoggerFactory([logs]);

        try
        {
            var audit = new AuthAudit(factory.CreateLogger<AuthAudit>(), clock);
            for (var i = 0; i < 10000; i++)
            {
                audit.Record(AuthEvent.AUTH_PASSWORD_FAILED, Guid.NewGuid());
            }

            Assert.That(audit.GroupCount, Is.EqualTo(AuthAudit.MaximumGroups + 1));
            Assert.That(Records(output.ToString()).Length, Is.EqualTo(AuthAudit.MaximumGroups + 1));

            audit.Record(AuthEvent.AUTH_ALL_SESSIONS_REVOKED, Guid.NewGuid());

            using var worker = new AuthAuditWorker(audit, clock);
            await worker.StartAsync(CancellationToken.None);
            await worker.StopAsync(CancellationToken.None);

            var records = Records(output.ToString());

            Assert.That(records.Where(x => x.GetProperty("errorCode").GetString() == "AUTH_PASSWORD_FAILED").Sum(x => x.GetProperty("Count").GetInt64()), Is.EqualTo(10000));
            Assert.That(audit.GroupCount, Is.Zero);
            Assert.That(records.Count(x => x.GetProperty("errorCode").GetString() == "AUTH_ALL_SESSIONS_REVOKED"), Is.EqualTo(1));
            Assert.That(records.Length, Is.LessThanOrEqualTo(AuthAudit.MaximumGroups + 3));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>隔離アプリが出力した監査ファイルを連結して照合する。</summary>
    private static string ReadLogs(TestApplication app) => string.Join("", Directory.EnumerateFiles(Path.Combine(app.DataDirectory, "logs"), "*.jsonl").Select(File.ReadAllText));

    /// <summary>各ログ行を独立したJSONとして読み、解析用Documentの寿命から切り離す。</summary>
    private static JsonElement[] Records(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(x =>
    {
        using var document = JsonDocument.Parse(x);
        return document.RootElement.Clone();
    }).ToArray();
}
