using System.Buffers.Binary;
using System.Text;
using GoodsManager.Web.Images;
using SkiaSharp;

namespace GoodsManager.Tests;

public sealed class ImageConversionTests
{
    private string directory = null!;
    [SetUp] public void Start() => directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GoodsManager.ImageTests", Guid.NewGuid().ToString("N"))).FullName;
    [TearDown] public void Stop() => Directory.Delete(directory, true);
    public static byte[] Png(int width = 640, int height = 320)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul); using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.Transparent);
        using var red = new SKPaint { Color = SKColors.Red }; using var blue = new SKPaint { Color = SKColors.Blue };
        canvas.DrawRect(new SKRect(0, 0, width / 2f, height / 2f), red); canvas.DrawRect(new SKRect(width / 2f, 0, width, height / 2f), blue);
        using var pixmap = bitmap.PeekPixels(); using var encoded = pixmap.Encode(new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, 6)); return encoded!.ToArray();
    }
    private ImageJob Job(byte[] bytes, int display = 2048, int thumb = 320, long pixels = 40_000_000)
    {
        var input = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".input"); File.WriteAllBytes(input, bytes);
        var output = Directory.CreateDirectory(Path.Combine(directory, Guid.NewGuid().ToString("N"))).FullName;
        return new(input, output, new(false, display, 90, thumb, 75, ImagePolicy.Version), 30 * 1024 * 1024, pixels, 16384);
    }
    [Test]
    public void PngConvertsToWebpPreservesTransparencyAspectAndNeverUpscales()
    {
        var job = Job(Png()); var result = ImageConverter.Convert(job);
        Assert.That((result.DisplayWidth, result.DisplayHeight), Is.EqualTo((640, 320)));
        Assert.That((result.ThumbnailWidth, result.ThumbnailHeight), Is.EqualTo((320, 160)));
        using var display = SKBitmap.Decode(Path.Combine(job.OutputDirectory, "display.webp"));
        Assert.That(display.GetPixel(500, 250).Alpha, Is.Zero); Assert.That(display.GetPixel(100, 50).Red, Is.GreaterThan(240));
        using var codec = SKCodec.Create(Path.Combine(job.OutputDirectory, "display.webp")); Assert.That(codec.EncodedFormat, Is.EqualTo(SKEncodedImageFormat.Webp));
        var smaller = Job(Png(), 100, 50); var resized = ImageConverter.Convert(smaller); Assert.That((resized.DisplayWidth, resized.DisplayHeight), Is.EqualTo((100, 50)));
    }
    [Test]
    public void PolicySerializationIsStableAndDoesNotIncludeComputedProperties()
    {
        var policy = ImagePolicy.From(new ImageSettings()); Assert.That(policy.Json, Does.Not.Contain("Hash").And.Not.Contain("Json"));
        Assert.That(System.Text.Json.JsonSerializer.Deserialize<ImagePolicy>(policy.Json)!.Hash, Is.EqualTo(policy.Hash));
        Assert.That((policy with { KeepOriginal = true }).Hash, Is.Not.EqualTo(policy.Hash));
        Assert.That((policy with { DisplayQuality = 83 }).Hash, Is.Not.EqualTo(policy.Hash));
    }
    [Test]
    public void PixelLimitsTruncatedAndUnsupportedImagesAreRejectedBeforePublication()
    {
        var tooLarge = Job(Png(), pixels: 1000); Assert.That(Assert.Throws<ImageProblem>(() => ImageConverter.Convert(tooLarge))!.Message, Is.EqualTo("IMAGE_PIXEL_LIMIT"));
        Assert.That(Directory.EnumerateFiles(tooLarge.OutputDirectory), Is.Empty);
        Assert.Throws<ImageProblem>(() => ImageConverter.Convert(Job(Png()[..^12])));
        Assert.That(Assert.Throws<ImageProblem>(() => ImageConverter.Convert(Job("GIF89a"u8.ToArray())))!.Message, Is.EqualTo("IMAGE_UNSUPPORTED_FORMAT"));
        Assert.Throws<ImageProblem>(() => ImageConverter.Convert(Job("<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray())));
    }
    [Test]
    public void AnimatedContainerDeclarationsAreRejectedEvenIfDecoderWouldIgnoreFrames()
    {
        var source = Png(); var animated = new byte[source.Length + 20]; source.AsSpan(0, 33).CopyTo(animated); BinaryPrimitives.WriteUInt32BigEndian(animated.AsSpan(33, 4), 8); "acTL"u8.CopyTo(animated.AsSpan(37)); BinaryPrimitives.WriteUInt32BigEndian(animated.AsSpan(41, 4), 2); source.AsSpan(33).CopyTo(animated.AsSpan(53));
        Assert.That(Assert.Throws<ImageProblem>(() => ImageConverter.Convert(Job(animated)))!.Message, Is.EqualTo("IMAGE_ANIMATED"));
        byte[] webp = [.. "RIFF"u8, 22, 0, 0, 0, .. "WEBPVP8X"u8, 10, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        Assert.That(Assert.Throws<ImageProblem>(() => ImageConverter.Convert(Job(webp)))!.Message, Is.EqualTo("IMAGE_ANIMATED"));
    }
    [TestCase(1, 640, 320)]
    [TestCase(2, 640, 320)]
    [TestCase(3, 640, 320)]
    [TestCase(4, 640, 320)]
    [TestCase(5, 320, 640)]
    [TestCase(6, 320, 640)]
    [TestCase(7, 320, 640)]
    [TestCase(8, 320, 640)]
    public void ExifOrientationIsAppliedAndNotCopiedToWebp(int orientation, int width, int height)
    {
        using var bitmap = SKBitmap.Decode(Png()); using var pixmap = bitmap.PeekPixels(); using var jpeg = pixmap.Encode(new SKJpegEncoderOptions(90)); var bytes = jpeg!.ToArray();
        byte[] exif = [.. "Exif\0\0II"u8, 42, 0, 8, 0, 0, 0, 1, 0, 18, 1, 3, 0, 1, 0, 0, 0, (byte)orientation, 0, 0, 0, 0, 0, 0, 0];
        var source = new byte[bytes.Length + exif.Length + 4]; bytes.AsSpan(0, 2).CopyTo(source); source[2] = 255; source[3] = 225; BinaryPrimitives.WriteUInt16BigEndian(source.AsSpan(4, 2), (ushort)(exif.Length + 2)); exif.CopyTo(source.AsSpan(6)); bytes.AsSpan(2).CopyTo(source.AsSpan(exif.Length + 6));
        var job = Job(source); var result = ImageConverter.Convert(job); Assert.That((result.DisplayWidth, result.DisplayHeight), Is.EqualTo((width, height)));
        using var rendered = SKBitmap.Decode(Path.Combine(job.OutputDirectory, "display.webp"));
        var corners = new[] { (width / 4, height / 4), (3 * width / 4, height / 4), (3 * width / 4, 3 * height / 4), (width / 4, 3 * height / 4) };
        var redCorner = new[] { 0, 1, 2, 3, 0, 1, 2, 3 }[orientation - 1]; var blueCorner = new[] { 1, 0, 3, 2, 3, 2, 1, 0 }[orientation - 1];
        Assert.That(rendered.GetPixel(corners[redCorner].Item1, corners[redCorner].Item2).Red, Is.GreaterThan(230));
        Assert.That(rendered.GetPixel(corners[blueCorner].Item1, corners[blueCorner].Item2).Blue, Is.GreaterThan(230));
        var output = File.ReadAllBytes(Path.Combine(job.OutputDirectory, "display.webp")); Assert.That(Encoding.ASCII.GetString(output), Does.Not.Contain("Exif"));
        using var encoded = SKCodec.Create(Path.Combine(job.OutputDirectory, "display.webp")); Assert.That(encoded.EncodedOrigin, Is.EqualTo(SKEncodedOrigin.TopLeft));
    }
    [Test]
    public async Task ActualChildProcessUsesNativeCodecAndReturnsCompletedResult()
    {
        var job = Job(Png()); var result = await new IsolatedImageConverter(new ImageLimits()).ConvertAsync(job, default);
        Assert.That(result.MediaType, Is.EqualTo("image/png")); Assert.That(File.Exists(Path.Combine(job.OutputDirectory, "display.webp")), Is.True);
    }
    [Test]
    public async Task TimeoutKillsChildWithoutPublishingSuccess()
    {
        var job = Job(Png()); var converter = new IsolatedImageConverter(new ImageLimits { ConversionTimeout = TimeSpan.Zero });
        var error = Assert.ThrowsAsync<ImageProblem>(async () => await converter.ConvertAsync(job, default)); Assert.That(error!.Message, Is.EqualTo("IMAGE_TIMEOUT"));
        await Task.CompletedTask;
    }
}
