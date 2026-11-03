using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace GoodsManager.Tests;

/// <summary>TestServerでは再現できない、実Kestrelの認証本文上限を検証する。</summary>
public sealed class AuthBodyLimitTests
{
    /// <summary>宣言長・逐次転送・HTTP/2で本文制限を確認し、チャンクの付加情報も上限へ含める。</summary>
    [TestCase("length", 65536, HttpStatusCode.Unauthorized)]
    [TestCase("length", 65537, HttpStatusCode.RequestEntityTooLarge)]
    [TestCase("stream", 65500, HttpStatusCode.Unauthorized)]
    [TestCase("stream", 65536, HttpStatusCode.RequestEntityTooLarge)]
    [TestCase("stream", 65537, HttpStatusCode.RequestEntityTooLarge)]
    [TestCase("chunked", 65500, HttpStatusCode.Unauthorized)]
    [TestCase("chunked", 65536, HttpStatusCode.RequestEntityTooLarge)]
    [TestCase("chunked", 65537, HttpStatusCode.RequestEntityTooLarge)]
    [TestCase("http2", 65536, HttpStatusCode.Unauthorized)]
    [TestCase("http2", 65537, HttpStatusCode.RequestEntityTooLarge)]
    [TestCase("chunked-boundary", 65536, HttpStatusCode.Unauthorized)]
    [TestCase("chunked-boundary", 65537, HttpStatusCode.RequestEntityTooLarge)]
    public async Task KestrelEnforcesBodyBoundary(string framing, int length, HttpStatusCode expected)
    {
        await using var app = new TestApplication();
        app.UseKestrel(options => options.Listen(IPAddress.Loopback, 0,
            endpoint => endpoint.Protocols = framing == "http2" ? HttpProtocols.Http2 : HttpProtocols.Http1));
        app.StartServer();
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("Kestrelの待受アドレスが取得できない");
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false, UseCookies = false })
        {
            BaseAddress = new Uri(addresses.Addresses.Single())
        };
        client.DefaultRequestVersion = framing == "http2" ? HttpVersion.Version20 : HttpVersion.Version11;
        client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
        // 配置時と同じHTTPS終端情報を渡し、試験用のループバック通信にはCSRFクッキーを明示する。
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        using var csrfResponse = await client.GetAsync("/api/v1/auth/csrf");
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cookie = string.Join("; ", csrfResponse.Headers.GetValues("Set-Cookie").Select(x => x.Split(';')[0]));

        const string prefix = "{\"loginId\":\"unknown\",\"password\":\"x\",\"padding\":\"";
        // KestrelはHTTP/1.1のチャンクサイズ行・区切りも数える。単一チャンクでは付加情報が13バイトになる。
        var payloadLength = framing == "chunked-boundary" ? length - 13 : length;
        var bytes = Encoding.UTF8.GetBytes(prefix + new string('x', payloadLength - prefix.Length - 2) + "\"}");
        if (framing == "chunked-boundary")
        {
            var chunk = Encoding.ASCII.GetBytes(bytes.Length.ToString("X", System.Globalization.CultureInfo.InvariantCulture) + "\r\n"
                + Encoding.UTF8.GetString(bytes) + "\r\n0\r\n\r\n");
            Assert.That(chunk.Length, Is.EqualTo(length));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var connection = new TcpClient();
            var address = client.BaseAddress ?? throw new InvalidOperationException("試験用URLが未設定");
            await connection.ConnectAsync(address.Host, address.Port, timeout.Token);
            await using var stream = connection.GetStream();
            var headers = $"POST /api/v1/auth/password HTTP/1.1\r\nHost: {address.Authority}\r\nConnection: close\r\nX-Forwarded-Proto: https\r\nCookie: {cookie}\r\nX-CSRF-Token: {csrf.GetProperty("token").GetString()}\r\nContent-Type: application/json\r\nTransfer-Encoding: chunked\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), timeout.Token);
            await stream.WriteAsync(chunk, timeout.Token);
            using var reader = new StreamReader(stream, Encoding.ASCII);
            var statusLine = await reader.ReadLineAsync(timeout.Token);
            Assert.That(statusLine, Does.StartWith($"HTTP/1.1 {(int)expected} "));
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/password")
        {
            Version = client.DefaultRequestVersion,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = framing == "length" ? new ByteArrayContent(bytes) : new StreamContent(new UnseekableBody(new MemoryStream(bytes)))
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        Assert.That(request.Content.Headers.ContentLength, framing == "length" ? Is.EqualTo(length) : Is.Null);
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("X-CSRF-Token", csrf.GetProperty("token").GetString());
        if (framing == "chunked")
        {
            request.Headers.TransferEncodingChunked = true;
        }

        using var response = await client.SendAsync(request);
        Assert.That(response.Version, Is.EqualTo(client.DefaultRequestVersion));
        Assert.That(response.StatusCode, Is.EqualTo(expected), await response.Content.ReadAsStringAsync());
    }

    /// <summary>HttpClientに宣言長を計算させず、逐次転送する本文を再現する。</summary>
    private sealed class UnseekableBody(Stream inner) : Stream
    {
        /// <summary>逐次読み取りだけを許可する。</summary>
        public override bool CanRead => true;
        /// <summary>宣言長を事前計算させない。</summary>
        public override bool CanSeek => false;
        /// <summary>試験本文の書き換えを許可しない。</summary>
        public override bool CanWrite => false;
        /// <summary>総バイト数の問い合わせを拒否する。</summary>
        public override long Length => throw new NotSupportedException();
        /// <summary>転送位置の参照と巻き戻しを拒否する。</summary>
        public override long Position
        {
            get => throw new NotSupportedException(); set => throw new NotSupportedException();
        }

        /// <summary>本文を順に読み取る。</summary>
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        /// <summary>ネットワークへの転送に使う非同期読み取りを内側へ渡す。</summary>
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);

        /// <summary>読み取り専用のためフラッシュを拒否する。</summary>
        public override void Flush() => throw new NotSupportedException();

        /// <summary>読み取り位置の変更を拒否する。</summary>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <summary>本文の伸縮を拒否する。</summary>
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <summary>本文への書き込みを拒否する。</summary>
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <summary>要求終了時に内側の試験本文も解放する。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
