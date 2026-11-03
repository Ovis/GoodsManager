using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoodsManager.Web.Auth;
using GoodsManager.Web.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using static GoodsManager.Tests.AuthenticationTests;

namespace GoodsManager.Tests;

/// <summary>初期登録用秘密の公開防止・失効・再起動時の扱いを検証する。</summary>
public sealed class SetupTokenTests
{
    /// <summary>誤入力や検証失敗で秘密を消費せず、登録成功後には再利用を拒否する。</summary>
    [Test]
    public async Task TokenIsNeverReturnedAndOnlySuccessfulSetupConsumesIt()
    {
        await using var app = new TestApplication();
        using var client = Client(app);
        var path = Path.Combine(app.DataDirectory, "setup-token.txt");
        var token = File.ReadLines(path).First();

        var status = await client.GetStringAsync("/api/v1/setup/status");

        Assert.That(status.Contains(token), Is.False);

        foreach (var value in new string?[] { null, "", new('0', 64) })
        {
            Assert.That((await SendAsync(client, "setup", new
            {
                loginId = "owner",
                password = Password,
                setupToken = value
            })).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        }

        Assert.That(File.Exists(path), Is.True);
        Assert.That((await SendAsync(client, "setup", new
        {
            loginId = "owner",
            password = "short",
            setupToken = token
        })).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(File.Exists(path), Is.True);

        var success = await SetupAsync(client);

        Assert.That(success.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That((await success.Content.ReadAsStringAsync()).Contains(token), Is.False);
        Assert.That(File.Exists(path), Is.False);

        await File.WriteAllTextAsync(path, token + "\n" + app.Clock.GetUtcNow().AddHours(24).ToString("O") + "\n");

        using var attacker = Client(app);

        Assert.That((await SendAsync(attacker, "setup", new
        {
            loginId = "other",
            password = Password,
            setupToken = token
        })).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));

        foreach (var log in Directory.EnumerateFiles(Path.Combine(app.DataDirectory, "logs")))
        {
            Assert.That(File.ReadAllText(log).Contains(token), Is.False);
        }
    }

    /// <summary>有効な秘密を再起動で変えず、24時間経過後だけ再発行する。</summary>
    [Test]
    public async Task RestartPreservesValidTokenAndRegeneratesOnlyAfterExpiry()
    {
        await using var app = new TestApplication();
        using var client = Client(app);
        var path = Path.Combine(app.DataDirectory, "setup-token.txt");
        var original = File.ReadAllText(path);
        var token = original.Split('\n')[0];
        using var runtime = new DatabaseRuntime(app.DataDirectory);
        var next = new SetupToken(runtime, app.Clock, NullLogger<SetupToken>.Instance);
        next.Initialize(false);

        Assert.That(File.ReadAllText(path) == original, Is.True);
        Assert.That(next.Verify(token), Is.True);

        app.Clock.Advance(TimeSpan.FromHours(24));

        Assert.That(next.Verify(token), Is.False);

        next.Initialize(false);

        Assert.That(File.ReadAllText(path) == original, Is.False);
        Assert.That(next.Verify(token), Is.False);

        next.Initialize(true);

        Assert.That(File.Exists(path), Is.False);
    }

    /// <summary>秘密ファイルの消失・破損・読み取り失敗で初期登録を許可しない。</summary>
    [Test]
    public async Task MissingCorruptAndUnreadableFilesFailClosed()
    {
        await using var app = new TestApplication();
        using var client = Client(app);
        var tokens = app.Services.GetRequiredService<SetupToken>();
        var path = Path.Combine(app.DataDirectory, "setup-token.txt");
        var token = File.ReadLines(path).First();
        File.Delete(path);

        Assert.That(tokens.Verify(token), Is.False);

        await File.WriteAllTextAsync(path, "broken");

        Assert.That(tokens.Verify(token), Is.False);
        Assert.Throws<InvalidDataException>(() => tokens.Initialize(false));

        File.Delete(path);
        Directory.CreateDirectory(path);

        Assert.That(tokens.Verify(token), Is.False);
        Assert.That((await SendAsync(client, "setup", new
        {
            loginId = "owner",
            password = Password,
            setupToken = token
        })).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }
}
