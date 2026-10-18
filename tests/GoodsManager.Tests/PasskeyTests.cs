using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoodsManager.Web.Auth;
using GoodsManager.Web.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using static GoodsManager.Tests.AuthenticationTests;

namespace GoodsManager.Tests;

/// <summary>試験用認証器の実署名をIdentity標準ハンドラーで検証</summary>
public sealed class PasskeyTests
{
    /// <summary>空でも拡張結果が必須であり、正しいJSONなら標準検証で登録できることを確認</summary>
    [Test]
    public async Task RegistrationRequiresClientExtensionResultsEvenWhenEmpty()
    {
        await using var app = new TestApplication();
        using var client = Client(app);
        await SetupAsync(client);
        using var authenticator = new TestAuthenticator();
        var options = await RegistrationOptionsAsync(client);
        var credential = System.Text.Json.Nodes.JsonNode.Parse(authenticator.Create(options.GetProperty("publicKeyOptions")).GetRawText())!.AsObject();
        credential.Remove("clientExtensionResults");

        var response = await SendAsync(client, "account/passkeys", new { challengeId = options.GetProperty("challengeId"), credential, displayName = "確認" });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(problem.GetProperty("reason").GetString(), Is.EqualTo("CREDENTIAL_JSON_INVALID"));

        // 失敗時もチャレンジは消費されるため、新しい選択肢からやり直す
        options = await RegistrationOptionsAsync(client);
        response = await SendAsync(client, "account/passkeys", new
        {
            challengeId = options.GetProperty("challengeId"),
            credential = authenticator.Create(options.GetProperty("publicKeyOptions")),
            displayName = "確認"
        });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync());
    }

    /// <summary>一覧の最新ETagを取得し、標準ユーザー更新後の版を使う</summary>
    public static async Task<string> EtagAsync(HttpClient client, string id) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/account/passkeys")).EnumerateArray().Single(x => x.GetProperty("id").GetString() == id).GetProperty("etag").GetString()!;

    /// <summary>登録の本人確認と標準選択肢を取得</summary>
    private static async Task<JsonElement> RegistrationOptionsAsync(HttpClient client)
    {
        var response = await SendAsync(client, "account/passkeys/options", new { verification = new { password = Password } });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>());
    }

    /// <summary>試験用認証器を登録</summary>
    private static async Task RegisterAsync(HttpClient client, TestAuthenticator authenticator, string name)
    {
        var options = await RegistrationOptionsAsync(client);
        var response = await SendAsync(client, "account/passkeys", new { challengeId = options.GetProperty("challengeId"), credential = authenticator.Create(options.GetProperty("publicKeyOptions")), displayName = name });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync());
    }

    /// <summary>複数登録、保持ありのログイン、改名、既存パスキーによる削除</summary>
    [Test]
    public async Task MultiplePasskeysAuthenticateAndManageThroughStandardIdentity()
    {
        await using var app = new TestApplication();
        using var owner = Client(app);
        await SetupAsync(owner);
        using var first = new TestAuthenticator();
        using var second = new TestAuthenticator();
        await RegisterAsync(owner, first, "端末😀");
        await RegisterAsync(owner, second, "予備");
        var list = await owner.GetFromJsonAsync<JsonElement>("/api/v1/account/passkeys");
        Assert.That(list.GetArrayLength(), Is.EqualTo(2));

        using var loginClient = Client(app);
        var response = await SendAsync(loginClient, "auth/passkey/options", new { rememberMe = true });
        var options = await response.Content.ReadFromJsonAsync<JsonElement>();
        var credential = first.Get(options.GetProperty("publicKeyOptions"));
        var loggedIn = await SendAsync(loginClient, "auth/passkey/verify", new { challengeId = options.GetProperty("challengeId"), credential });
        Assert.That(loggedIn.StatusCode, Is.EqualTo(HttpStatusCode.OK), await loggedIn.Content.ReadAsStringAsync());
        Assert.That(loggedIn.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Host-GoodsManager=")).Contains("expires="), Is.True);
        var replay = await SendAsync(loginClient, "auth/passkey/verify", new { challengeId = options.GetProperty("challengeId"), credential });
        Assert.That(replay.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        Assert.That((await SendAsync(owner, "account/passkeys/" + second.Id, new { displayName = "予備変更" }, HttpMethod.Put, await EtagAsync(owner, second.Id))).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var proofResponse = await SendAsync(owner, "auth/reauthenticate", new { purpose = "passkey-delete:" + second.Id });
        var proof = await proofResponse.Content.ReadFromJsonAsync<JsonElement>();
        var deleted = await SendAsync(owner, "account/passkeys/" + second.Id, new { challengeId = proof.GetProperty("challengeId"), credential = first.Get(proof.GetProperty("publicKeyOptions")) }, HttpMethod.Delete, await EtagAsync(owner, second.Id));
        Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), await deleted.Content.ReadAsStringAsync());
        Assert.That((await owner.GetFromJsonAsync<JsonElement>("/api/v1/account/passkeys")).GetArrayLength(), Is.EqualTo(1));

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.OpenConnectionAsync();
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT typeof(CredentialId)||','||typeof(Data)||','||json_valid(Data) FROM AspNetUserPasskeys";
        Assert.That(await command.ExecuteScalarAsync(), Is.EqualTo("blob,text,1"));
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var passkey = (await users.GetPasskeysAsync(await db.Users.SingleAsync())).Single();
        Assert.That(passkey.SignCount, Is.EqualTo(2));
    }

    /// <summary>登録時のオリジン、RP、チャレンジ、本人確認フラグを標準検証で拒否</summary>
    [TestCase("origin")]
    [TestCase("rp")]
    [TestCase("challenge")]
    [TestCase("verification")]
    public async Task StandardHandlerRejectsInvalidAttestation(string invalid)
    {
        await using var app = new TestApplication();
        using var client = Client(app);
        await SetupAsync(client);
        using var authenticator = new TestAuthenticator();
        var options = await RegistrationOptionsAsync(client);
        var credential = authenticator.Create(options.GetProperty("publicKeyOptions"),
            origin: invalid == "origin" ? "https://evil.localhost:7443" : "https://localhost:7443",
            rpId: invalid == "rp" ? "evil.localhost" : "localhost",
            challenge: invalid == "challenge" ? Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode("wrong-challenge"u8.ToArray()) : null,
            verified: invalid != "verification");
        var response = await SendAsync(client, "account/passkeys", new { challengeId = options.GetProperty("challengeId"), credential, displayName = "不正" });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), await response.Content.ReadAsStringAsync());
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var expectedReason = invalid switch
        {
            "origin" => "ORIGIN_MISMATCH",
            "rp" => "RP_ID_MISMATCH",
            "challenge" => "CHALLENGE_MISMATCH",
            _ => "USER_NOT_VERIFIED"
        };
        Assert.That(problem.GetProperty("reason").GetString(), Is.EqualTo(expectedReason));
        Assert.That(problem.GetRawText(), Does.Not.Contain("evil.localhost").And.Not.Contain("wrong-challenge"));
        Assert.That((await client.GetFromJsonAsync<JsonElement>("/api/v1/account/passkeys")).GetArrayLength(), Is.Zero);
    }

    /// <summary>期限切れ、別用途への流用、署名改ざんを拒否</summary>
    [Test]
    public async Task ChallengesExpireAndCannotAuthorizeDifferentOperation()
    {
        await using var app = new TestApplication();
        using var client = Client(app);
        await SetupAsync(client);
        using var authenticator = new TestAuthenticator();
        await RegisterAsync(client, authenticator, "本人確認用");
        var options = await RegistrationOptionsAsync(client);
        var credential = authenticator.Create(options.GetProperty("publicKeyOptions"));
        app.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.That((await SendAsync(client, "account/passkeys", new { challengeId = options.GetProperty("challengeId"), credential, displayName = "期限切れ" })).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var response = await SendAsync(client, "auth/reauthenticate", new { purpose = "passkey-add" });
        var proof = await response.Content.ReadFromJsonAsync<JsonElement>();
        var password = await SendAsync(client, "account/password", new { newPassword = "changed-password", verification = new { challengeId = proof.GetProperty("challengeId"), credential = authenticator.Get(proof.GetProperty("publicKeyOptions")) } });
        Assert.That(password.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        response = await SendAsync(client, "auth/passkey/options", new { rememberMe = false });
        options = await response.Content.ReadFromJsonAsync<JsonElement>();
        var invalid = await SendAsync(client, "auth/passkey/verify", new { challengeId = options.GetProperty("challengeId"), credential = authenticator.Get(options.GetProperty("publicKeyOptions"), badSignature: true) });
        Assert.That(invalid.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    /// <summary>既存パスキーによるパスワード変更と、復旧時のパスキー削除を検証</summary>
    [Test]
    public async Task PasskeyCanVerifyPasswordChangeAndRecoveryCanRemovePasskeys()
    {
        await using var app = new TestApplication();
        using var client = Client(app);
        await SetupAsync(client);
        using var authenticator = new TestAuthenticator();
        await RegisterAsync(client, authenticator, "本人確認");
        var response = await SendAsync(client, "auth/reauthenticate", new { purpose = "password" });
        var options = await response.Content.ReadFromJsonAsync<JsonElement>();
        var changed = await SendAsync(client, "account/password", new { newPassword = "new-password-123", verification = new { challengeId = options.GetProperty("challengeId"), credential = authenticator.Get(options.GetProperty("publicKeyOptions")) } });
        Assert.That(changed.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        await using var scope = app.Services.CreateAsyncScope();
        Assert.That((await scope.ServiceProvider.GetRequiredService<AccountRecovery>().ResetAsync("recovered-password", true)).Succeeded, Is.True);
        Assert.That(await scope.ServiceProvider.GetRequiredService<AppDbContext>().UserPasskeys.CountAsync(), Is.Zero);
    }

    /// <summary>既存パスキーの本人確認で別のパスキーを追加し、復旧前の選択肢を失効</summary>
    [Test]
    public async Task ExistingPasskeyVerifiesEnrollmentAndRecoveryInvalidatesPendingLogin()
    {
        await using var app = new TestApplication();
        using var client = Client(app);
        await SetupAsync(client);
        using var first = new TestAuthenticator();
        using var second = new TestAuthenticator();
        await RegisterAsync(client, first, "最初");
        var response = await SendAsync(client, "auth/reauthenticate", new { purpose = "passkey-add" });
        var proof = await response.Content.ReadFromJsonAsync<JsonElement>();
        response = await SendAsync(client, "account/passkeys/options", new { verification = new { challengeId = proof.GetProperty("challengeId"), credential = first.Get(proof.GetProperty("publicKeyOptions")) } });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var enrollment = await response.Content.ReadFromJsonAsync<JsonElement>();
        response = await SendAsync(client, "account/passkeys", new { challengeId = enrollment.GetProperty("challengeId"), credential = second.Create(enrollment.GetProperty("publicKeyOptions")), displayName = "追加" });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        using var login = Client(app);
        response = await SendAsync(login, "auth/passkey/options", new { rememberMe = false });
        var options = await response.Content.ReadFromJsonAsync<JsonElement>();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AccountRecovery>().ResetAsync("recovered-password", false);
            Assert.That(await scope.ServiceProvider.GetRequiredService<AppDbContext>().UserPasskeys.CountAsync(), Is.EqualTo(2));
        }

        response = await SendAsync(login, "auth/passkey/verify", new { challengeId = options.GetProperty("challengeId"), credential = first.Get(options.GetProperty("publicKeyOptions")) });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    /// <summary>別ブラウザへの移送と、消費後に元クッキーを復元する再利用を拒否</summary>
    [Test]
    public async Task ChallengeIsBoundToBrowserAndConsumedOnServer()
    {
        await using var app = new TestApplication();
        using var owner = Client(app);
        await SetupAsync(owner);
        using var authenticator = new TestAuthenticator();
        await RegisterAsync(owner, authenticator, "端末");
        using var login = Client(app);
        var response = await SendAsync(login, "auth/passkey/options", new { rememberMe = false });
        var cookie = string.Join("; ", response.Headers.GetValues("Set-Cookie").Select(x => x.Split(';')[0]));
        var options = await response.Content.ReadFromJsonAsync<JsonElement>();
        var request = new { challengeId = options.GetProperty("challengeId"), credential = authenticator.Get(options.GetProperty("publicKeyOptions")) };
        using var other = Client(app);
        Assert.That((await SendAsync(other, "auth/passkey/verify", request)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await SendAsync(login, "auth/passkey/verify", request)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using var replay = Client(app);
        replay.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.That((await SendAsync(replay, "auth/passkey/verify", request)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    /// <summary>SQ08/SQ10: データ入り旧スキーマの再構築でBLOB・JSON・制約と認証を維持</summary>
    [Test]
    public async Task MetadataMigrationPreservesExistingPasskeyAndRejectsInvalidJsonContent()
    {
        string directory;
        using var authenticator = new TestAuthenticator();
        await using (var first = new TestApplication { PreserveData = true })
        {
            directory = first.DataDirectory;
            using var owner = Client(first);
            await SetupAsync(owner);
            await RegisterAsync(owner, authenticator, "移行前😀");
        }

        var connectionString = new DatabaseRuntime(directory).ConnectionString;
        await using (var previous = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options))
        {
            var migrations = (await previous.Database.GetAppliedMigrationsAsync()).ToArray();
            await previous.GetService<IMigrator>().MigrateAsync(migrations[^2]);
        }

        await using var upgraded = new TestApplication(directory);
        using var login = Client(upgraded);
        var response = await SendAsync(login, "auth/passkey/options", new { rememberMe = false });
        var options = await response.Content.ReadFromJsonAsync<JsonElement>();
        response = await SendAsync(login, "auth/passkey/verify", new { challengeId = options.GetProperty("challengeId"), credential = authenticator.Get(options.GetProperty("publicKeyOptions")) });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await login.GetFromJsonAsync<JsonElement>("/api/v1/account/passkeys"))[0].GetProperty("displayName").GetString(), Is.EqualTo("移行前😀"));
        Assert.That(Directory.GetFiles(Path.Combine(directory, "pre-migration"), "*.zip"), Is.Not.Empty);
        await using var scope = upgraded.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var sql in new[]
        {
            "UPDATE AspNetUserPasskeys SET Data=NULL",
            "UPDATE AspNetUserPasskeys SET Data='null'",
            "UPDATE AspNetUserPasskeys SET Data='invalid-json'",
            "UPDATE AspNetUserPasskeys SET Data=json_set(Data,'$.Name',123)",
            "UPDATE AspNetUserPasskeys SET Data=json_set(Data,'$.Name',char(0))",
            "UPDATE AspNetUserPasskeys SET CredentialId='not-a-blob'"
        })
        {
            Assert.ThrowsAsync<SqliteException>(async () => await db.Database.ExecuteSqlRawAsync(sql));
        }

        var user = await db.Users.SingleAsync();
        var passkey = (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().GetPasskeysAsync(user)).Single();
        passkey.Name = "\0";
        Assert.Throws<InvalidDataException>(() => PasskeyMetadata.RequireValid(passkey));
    }
}
