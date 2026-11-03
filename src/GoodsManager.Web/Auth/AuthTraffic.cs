using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace GoodsManager.Web.Auth;

/// <summary>送信元情報を使わず、単一インスタンス全体の認証負荷を制限する。</summary>
public sealed class AuthTrafficOptions
{
    /// <summary>パスワード検証や設定変更など、書き込み系認証要求の1分あたりの許可数。</summary>
    public int HeavyPerMinute { get; set; } = 60;

    /// <summary>重い認証処理を同時実行できる要求数。</summary>
    public int HeavyConcurrency { get; set; } = 2;

    /// <summary>重い認証処理の実行枠を待てる要求数。満杯なら429で拒否する。</summary>
    public int HeavyQueue { get; set; } = 2;

    /// <summary>状態確認とCSRF取得を含む読み取り系認証要求の1分あたりの許可数。</summary>
    public int ReadPerMinute { get; set; } = 300;

    /// <summary>読み取り系認証処理を同時実行できる要求数。</summary>
    public int ReadConcurrency { get; set; } = 8;

    /// <summary>読み取り系認証処理の実行枠を待てる要求数。</summary>
    public int ReadQueue { get; set; } = 8;

    /// <summary>認証要求に許可する本文バイト数。Kestrelではチャンクの付加情報も対象になる。</summary>
    public int MaximumBodyBytes { get; set; } = 64 * 1024;

    /// <summary>起動時に負荷制限の正数条件と構成可能な範囲を検査する。</summary>
    public bool Valid => HeavyPerMinute is > 0 and <= 100000 && ReadPerMinute is > 0 and <= 100000
        && HeavyConcurrency is > 0 and <= 64 && ReadConcurrency is > 0 and <= 64
        && HeavyQueue is >= 0 and <= 1024 && ReadQueue is >= 0 and <= 1024 && MaximumBodyBytes is > 0 and <= 1024 * 1024;
}

/// <summary>認証要求を固定の処理種別に分け、全体の負荷と本文量を制限する。</summary>
public static class AuthTraffic
{
    /// <summary>パーティションキーは固定3種。IDやIPごとにメモリ上の項目を増やさない。</summary>
    public static string Group(HttpContext context) => context.Request.Path.StartsWithSegments("/api/v1/auth")
        || context.Request.Path.StartsWithSegments("/api/v1/setup") || context.Request.Path.StartsWithSegments("/api/v1/account")
        ? HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method) || HttpMethods.IsOptions(context.Request.Method) ? "read" : "heavy" : "none";

    /// <summary>設定値を起動時に検証し、頻度制限と同時実行・待機数の制限を連結する。</summary>
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
                    {
                        PermitLimit = key == "heavy" ? limits.HeavyPerMinute : limits.ReadPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    })
                    : RateLimitPartition.GetNoLimiter("none")),
                PartitionedRateLimiter.Create<HttpContext, string>(context => Group(context) is var group && group != "none"
                    ? RateLimitPartition.GetConcurrencyLimiter(group, key => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = key == "heavy" ? limits.HeavyConcurrency : limits.ReadConcurrency,
                        QueueLimit = key == "heavy" ? limits.HeavyQueue : limits.ReadQueue,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    })
                    : RateLimitPartition.GetNoLimiter("none")));
            options.OnRejected = async (rejected, cancellation) =>
            {
                rejected.HttpContext.RequestServices.GetRequiredService<AuthAudit>().Failure(rejected.HttpContext, AuthEvent.AUTH_RATE_LIMITED);
                rejected.HttpContext.Response.Headers.RetryAfter = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait)
                    ? Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture) : "60";
                await Results.Problem(statusCode: 429, title: "認証処理が混み合っています。しばらく待ってからやり直してください", extensions: new Dictionary<string, object?> { ["code"] = "AUTH_RATE_LIMITED" }).ExecuteAsync(rejected.HttpContext);
            };
        });
    }

    /// <summary>JSONを一時ファイルへ保存せず、宣言長と実際に読み取るバイト数を制限する。</summary>
    public static async Task LimitBodyAsync(HttpContext context, RequestDelegate next)
    {
        if (Group(context) == "none")
        {
            await next(context);
            return;
        }

        var maximum = context.RequestServices.GetRequiredService<IOptions<AuthTrafficOptions>>().Value.MaximumBodyBytes;
        if (context.Request.ContentLength > maximum)
        {
            throw new BadHttpRequestException("認証要求の本文上限", 413);
        }

        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false })
        {
            feature.MaxRequestBodySize = maximum;
        }

        // 現行のMVCとHTTP/2では標準上限超過がInvalidOperationExceptionとなり、500応答へ変わる。
        // 413を維持するため、BodyReaderが使うStreamでも読み取り量を検査する。
        var original = context.Request.Body;
        using var limited = new LimitedBody(original, maximum);
        context.Request.Body = limited;
        try
        {
            await next(context);
        }
        finally
        {
            context.Request.Body = original;
        }
    }

    /// <summary>MVCに本文を渡す前に累積読み取り量を検査し、超過を413として通知する。</summary>
    private sealed class LimitedBody(Stream inner, long maximum) : Stream
    {
        private long consumed;

        /// <summary>超過を検出する1バイトだけ余分に許可し、巨大な一括読み取りを避ける。</summary>
        private int Allow(int requested) => (int)Math.Min(requested, maximum - consumed + 1);

        /// <summary>読み取り済みバイト数を加算し、上限を超えた時点で本文処理を止める。</summary>
        private int Count(int read)
        {
            consumed += read;
            if (consumed > maximum)
            {
                throw new BadHttpRequestException("認証要求の本文上限", 413);
            }

            return read;
        }

        /// <summary>要求本文を同期読み取りし、累積量を上限と照合する。</summary>
        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, Allow(count)));

        /// <summary>要求本文を同期読み取りし、累積量を上限と照合する。</summary>
        public override int Read(Span<byte> buffer) => Count(inner.Read(buffer[..Allow(buffer.Length)]));

        /// <summary>非同期読み取りにも同期読み取りと同じ累積上限を適用する。</summary>
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => Count(await inner.ReadAsync(buffer[..Allow(buffer.Length)], cancellationToken));

        /// <summary>非同期読み取りにも同期読み取りと同じ累積上限を適用する。</summary>
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        /// <summary>内側の要求本文を読み取れることを示す。</summary>
        public override bool CanRead => true;

        /// <summary>累積量の巻き戻しを許可しない。</summary>
        public override bool CanSeek => false;

        /// <summary>要求本文への書き込みを許可しない。</summary>
        public override bool CanWrite => false;

        /// <summary>逐次転送のため総バイト数の問い合わせを拒否する。</summary>
        public override long Length => throw new NotSupportedException();

        /// <summary>読み取り位置の参照と変更を拒否し、累積量の検査を迂回させない。</summary>
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <summary>読み取り専用ラッパーのため出力処理を行わない。</summary>
        public override void Flush()
        {
        }

        /// <summary>上限検査済みの領域への移動を許可しない。</summary>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <summary>要求本文の伸縮を許可しない。</summary>
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <summary>要求本文の書き換えを許可しない。</summary>
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
