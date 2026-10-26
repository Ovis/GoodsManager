using System.Net;
using System.Text.Json;
using GoodsManager.Web.Operations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace GoodsManager.Tests;

/// <summary>ログの秘密排除・日付保持・容量分割と、配置側へ委ねる接続元制限</summary>
public sealed class OperationsTests
{
    /// <summary>自由文字列を標準出力とファイルから除き、実バイト数で分割</summary>
    [Test]
    public void LogOmitsFreeTextAndSplitsByActualBytes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "GoodsManager.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var output = new StringWriter(); using var logs = new OperationalLogging(directory, new TestClock(), output, 700);
            var logger = logs.CreateLogger("GoodsManager.Web.Operations"); var id = Guid.NewGuid();
            for (var i = 0; i < 12; i++) logger.LogError(new IOException("secret-exception"), "secret-literal {Memo} {Code} {Id}", "secret-value", "STORAGE_FAILED", id);
            logs.CreateLogger("Microsoft.EntityFrameworkCore.Database.Command").LogError("secret-sql {Parameters}", "secret-sql-value");
            var files = Directory.GetFiles(directory); Assert.That(files.Length, Is.GreaterThan(1));
            foreach (var file in files)
            {
                Assert.That(new FileInfo(file).Length, Is.LessThanOrEqualTo(700));
                Assert.That(File.ReadAllText(file), Does.Not.Contain("secret-"));
                foreach (var line in File.ReadLines(file)) Assert.DoesNotThrow(() => JsonDocument.Parse(line).Dispose());
            }
            var text = output.ToString(); Assert.That(text, Does.Not.Contain("secret-")); Assert.That(text, Does.Contain("STORAGE_FAILED")); Assert.That(text, Does.Contain(id.ToString())); Assert.That(text, Does.Contain("IOException"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    /// <summary>分割数ではなく30暦日で保持し、無関係なファイルを残す</summary>
    [Test]
    public void RetentionUsesDateInsteadOfFileCount()
    {
        var directory = Path.Combine(Path.GetTempPath(), "GoodsManager.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var clock = new TestClock(); using var logs = new OperationalLogging(directory, clock, new StringWriter()); var logger = logs.CreateLogger("GoodsManager.Web.Operations");
            logger.LogInformation("{Code}", "FIRST_DAY"); var first = Directory.GetFiles(directory).Single();
            clock.Advance(TimeSpan.FromDays(29)); logger.LogInformation("{Code}", "LAST_RETAINED_DAY"); Assert.That(File.Exists(first), Is.True);
            var unrelated = Path.Combine(directory, "goodsmanager-other.jsonl"); File.WriteAllText(unrelated, "unrelated");
            clock.Advance(TimeSpan.FromDays(1)); logger.LogInformation("{Code}", "NEW_DAY");
            Assert.That(File.Exists(first), Is.False); Assert.That(File.ReadAllText(unrelated), Is.EqualTo("unrelated"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    /// <summary>ファイル障害は秘密を含まない固定コードで標準出力へ記録</summary>
    [Test]
    public void FileFailureHasSafeFallback()
    {
        var directory = Path.Combine(Path.GetTempPath(), "GoodsManager.Tests", Guid.NewGuid().ToString("N"));
        var output = new StringWriter(); using var logs = new OperationalLogging(directory, new TestClock(), output);
        Directory.Delete(directory); File.WriteAllText(directory, "blocked");
        try
        {
            Assert.DoesNotThrow(() => logs.CreateLogger("GoodsManager.Web.Operations").LogError("{Code}", "STORAGE_FAILED"));
            Assert.That(logs.FileFailed, Is.True); Assert.That(output.ToString(), Does.Contain("LOG_STORAGE_FAILED"));
        }
        finally { File.Delete(directory); }
    }

    /// <summary>本番でも公開URLの制限は配置側へ委ね、HTTPS情報だけを接続元IPの登録なしで採用</summary>
    [TestCase("192.0.2.10", "internal:8080")]
    [TestCase("198.51.100.12", "localhost:7443")]
    [TestCase("2001:db8::10", "other.example.invalid:8443")]
    public async Task ForwardingOnlyChangesSchemeAndPreservesHostAndPeer(string peer, string host)
    {
        await using var app = new TestApplication { AuthenticationOrigin = "https://localhost:7443" };
        await using var production = app.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        var context = await production.Server.SendAsync(request =>
        {
            request.Request.Method = "GET"; request.Request.Path = "/api/v1/setup/status";
            request.Connection.RemoteIpAddress = IPAddress.Parse(peer); request.Request.Scheme = "http"; request.Request.Host = new HostString(host);
            request.Request.Headers["X-Forwarded-For"] = "198.51.100.1";
            request.Request.Headers["X-Forwarded-Proto"] = "https";
            request.Request.Headers["X-Forwarded-Host"] = "forwarded.example.invalid:9443";
        });
        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(context.Request.IsHttps, Is.True);
        Assert.That(context.Request.Host.Value, Is.EqualTo(host));
        Assert.That(context.Connection.RemoteIpAddress, Is.EqualTo(IPAddress.Parse(peer)));
        Assert.That(context.Request.Headers["X-Forwarded-For"].ToString(), Is.EqualTo("198.51.100.1"));
        Assert.That(context.Request.Headers["X-Forwarded-Host"].ToString(), Is.EqualTo("forwarded.example.invalid:9443"));
    }

    /// <summary>本番でも転送情報なしではHTTPのまま、複数段なら直近のProtoだけ反映</summary>
    [TestCase(null, false, "")]
    [TestCase("https, http", false, "https")]
    [TestCase("http, https", true, "http")]
    public async Task ForwardingDoesNotInventHttpsOrConsumeEarlierHops(string? proto, bool https, string remaining)
    {
        await using var app = new TestApplication { AuthenticationOrigin = "https://localhost:7443" };
        await using var production = app.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        var context = await production.Server.SendAsync(request =>
        {
            request.Request.Method = "GET"; request.Request.Path = "/api/v1/setup/status";
            request.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10"); request.Request.Scheme = "http";
            request.Request.Host = new HostString("internal:8080");
            request.Request.Headers["X-Forwarded-For"] = "198.51.100.1, 198.51.100.2";
            request.Request.Headers["X-Forwarded-Host"] = "earlier.example.invalid, latest.example.invalid";
            if (proto is not null) request.Request.Headers["X-Forwarded-Proto"] = proto;
        });
        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(context.Request.IsHttps, Is.EqualTo(https));
        Assert.That(context.Request.Host.Value, Is.EqualTo("internal:8080"));
        Assert.That(context.Connection.RemoteIpAddress, Is.EqualTo(IPAddress.Parse("192.0.2.10")));
        Assert.That(context.Request.Headers["X-Forwarded-Proto"].ToString(), Is.EqualTo(remaining));
        Assert.That(context.Request.Headers["X-Forwarded-For"].ToString(), Is.EqualTo("198.51.100.1, 198.51.100.2"));
        Assert.That(context.Request.Headers["X-Forwarded-Host"].ToString(), Is.EqualTo("earlier.example.invalid, latest.example.invalid"));
    }

    /// <summary>主要領域が塞がれた間は準備未完了、復旧後に正常へ戻る</summary>
    [Test]
    public async Task ReadinessDetectsWritableDirectoryFailureAndRecovery()
    {
        await using var app = new TestApplication(); using var client = app.CreateClient();
        Assert.That((await client.GetAsync("/health/ready")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var images = Path.Combine(app.DataDirectory, "images"); var held = images + ".held";
        Directory.Move(images, held); File.WriteAllText(images, "blocked");
        try
        {
            Assert.That((await client.GetAsync("/health/ready")).StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That((await client.GetAsync("/health/live")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
        finally { File.Delete(images); Directory.Move(held, images); }
        Assert.That((await client.GetAsync("/health/ready")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
