using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace GoodsManager.Web.Auth;

/// <summary>送信元情報を使わず、単一インスタンス全体の認証負荷を制限する。</summary>
public sealed class AuthTrafficOptions
{
    public int HeavyPerMinute { get; set; } = 60;
    public int HeavyConcurrency { get; set; } = 2;
    public int HeavyQueue { get; set; } = 2;
    public int ReadPerMinute { get; set; } = 300;
    public int ReadConcurrency { get; set; } = 8;
    public int ReadQueue { get; set; } = 8;
    public int MaximumBodyBytes { get; set; } = 64 * 1024;
    public bool Valid => HeavyPerMinute is > 0 and <= 100000 && ReadPerMinute is > 0 and <= 100000
        && HeavyConcurrency is > 0 and <= 64 && ReadConcurrency is > 0 and <= 64
        && HeavyQueue is >= 0 and <= 1024 && ReadQueue is >= 0 and <= 1024 && MaximumBodyBytes is > 0 and <= 1024 * 1024;
}

public static class AuthTraffic
{
    /// <summary>パーティションキーは固定3種。IDやIPごとにメモリ上の項目を増やさない。</summary>
    public static string Group(HttpContext context) => context.Request.Path.StartsWithSegments("/api/v1/auth")
        || context.Request.Path.StartsWithSegments("/api/v1/setup") || context.Request.Path.StartsWithSegments("/api/v1/account")
        ? HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method) || HttpMethods.IsOptions(context.Request.Method) ? "read" : "heavy" : "none";

    public static void AddAuthTraffic(this IServiceCollection services)
    {
        services.AddOptions<AuthTrafficOptions>().BindConfiguration("Authentication:Traffic").Validate(value => value.Valid, "認証負荷上限が範囲外").ValidateOnStart();
        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<AuthTrafficOptions>>((options, settings) =>
        {
            var limits = settings.Value;
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(context => Group(context) is var group && group != "none"
                    ? RateLimitPartition.GetSlidingWindowLimiter(group, key => new SlidingWindowRateLimiterOptions
                    { PermitLimit = key == "heavy" ? limits.HeavyPerMinute : limits.ReadPerMinute, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6, QueueLimit = 0, AutoReplenishment = true })
                    : RateLimitPartition.GetNoLimiter("none")),
                PartitionedRateLimiter.Create<HttpContext, string>(context => Group(context) is var group && group != "none"
                    ? RateLimitPartition.GetConcurrencyLimiter(group, key => new ConcurrencyLimiterOptions
                    { PermitLimit = key == "heavy" ? limits.HeavyConcurrency : limits.ReadConcurrency, QueueLimit = key == "heavy" ? limits.HeavyQueue : limits.ReadQueue, QueueProcessingOrder = QueueProcessingOrder.OldestFirst })
                    : RateLimitPartition.GetNoLimiter("none")));
            options.OnRejected = async (rejected, cancellation) =>
            {
                rejected.HttpContext.Response.Headers.RetryAfter = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait)
                    ? Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture) : "60";
                await Results.Problem(statusCode: 429, title: "認証処理が混み合っています。しばらく待ってからやり直してください", extensions: new Dictionary<string, object?> { ["code"] = "AUTH_RATE_LIMITED" }).ExecuteAsync(rejected.HttpContext);
            };
        });
    }

    /// <summary>JSONを一時ファイルへ保存せず、宣言長と実際に読み取るバイト数を制限する。</summary>
    public static async Task LimitBodyAsync(HttpContext context, RequestDelegate next)
    {
        if (Group(context) == "none") { await next(context); return; }
        var maximum = context.RequestServices.GetRequiredService<IOptions<AuthTrafficOptions>>().Value.MaximumBodyBytes;
        if (context.Request.ContentLength > maximum) throw new BadHttpRequestException("認証要求の本文上限", 413);
        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = maximum;
        var original = context.Request.Body;
        using var limited = new LimitedBody(original, maximum); context.Request.Body = limited;
        try { await next(context); } finally { context.Request.Body = original; }
    }

    private sealed class LimitedBody(Stream inner, long maximum) : Stream
    {
        private long consumed;
        private int Allow(int requested) => (int)Math.Min(requested, maximum - consumed + 1);
        private int Count(int read) { consumed += read; if (consumed > maximum) throw new BadHttpRequestException("認証要求の本文上限", 413); return read; }
        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, Allow(count)));
        public override int Read(Span<byte> buffer) => Count(inner.Read(buffer[..Allow(buffer.Length)]));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => Count(await inner.ReadAsync(buffer[..Allow(buffer.Length)], cancellationToken));
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
