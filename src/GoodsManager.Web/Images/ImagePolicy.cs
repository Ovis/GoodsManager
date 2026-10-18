using System.Security.Cryptography;
using System.Text.Json;
using GoodsManager.Web.Masters;

namespace GoodsManager.Web.Images;

public sealed record ImagePolicy(bool KeepOriginal, int DisplayMaxEdge, int DisplayQuality, int ThumbMaxEdge, int ThumbQuality, string ConverterVersion)
{
    public const string Version = "skia-4.153.1-v1-srgb-no-animation";
    public static ImagePolicy From(ImageSettings settings) => new(settings.KeepOriginal, settings.DisplayMaxEdge, settings.DisplayQuality, settings.ThumbMaxEdge, settings.ThumbQuality, Version);
    [System.Text.Json.Serialization.JsonIgnore]
    public string Json => JsonSerializer.Serialize(this);
    [System.Text.Json.Serialization.JsonIgnore]
    public string Hash => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Json)));
    public void Validate()
    {
        if (DisplayMaxEdge is < 1 or > 4096 || ThumbMaxEdge is < 1 or > 1024 || ThumbMaxEdge > DisplayMaxEdge || DisplayQuality is < 1 or > 100 || ThumbQuality is < 1 or > 100 || ConverterVersion != Version) throw new MasterProblem(400, "VALIDATION_FAILED", "imageSettings");
    }
}
public sealed class ImageLimits
{
    public long UploadMaxBytes { get; init; } = 30L * 1024 * 1024;
    public long MaxPixels { get; init; } = 40_000_000;
    public int MaxEdge { get; init; } = 16_384;
    public TimeSpan ConversionTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public long WorkerMemoryBytes { get; init; } = 512L * 1024 * 1024;
    public long TemporaryMaxBytes { get; init; } = 2L * 1024 * 1024 * 1024;
    public int TemporaryMaxCount { get; init; } = 100;
    public TimeSpan UploadLifetime { get; init; } = TimeSpan.FromHours(24);
    public TimeSpan ReplayLifetime { get; init; } = TimeSpan.FromDays(7);
    public TimeSpan GcGrace { get; init; } = TimeSpan.FromHours(24);
}
public sealed class ImageProblem(string code) : Exception(code);
