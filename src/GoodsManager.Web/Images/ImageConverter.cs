using System.Buffers.Binary;
using System.Text.Json;
using SkiaSharp;

namespace GoodsManager.Web.Images;

public sealed record ConvertedImage(string MediaType, int Width, int Height, int DisplayWidth, int DisplayHeight, int ThumbnailWidth, int ThumbnailHeight, int SourceWidth, int SourceHeight);
public sealed record ImageJob(string InputPath, string OutputDirectory, ImagePolicy Policy, long MaxBytes, long MaxPixels, int MaxEdge);

/// <summary>ネイティブデコードはこの子プロセス内だけで実行。DB・認証鍵を開かない。</summary>
public static class ImageConverter
{
    public static int RunCli(string[] args)
    {
        if (args.Length != 2) return 2;
        var job = JsonSerializer.Deserialize<ImageJob>(File.ReadAllText(args[1])) ?? throw new InvalidDataException();
        Directory.CreateDirectory(job.OutputDirectory);
        string? error = null; ConvertedImage? image = null;
        try { image = Convert(job); }
        catch (ImageProblem problem) { error = problem.Message; }
        catch (OutOfMemoryException) { error = "IMAGE_MEMORY_LIMIT"; }
        catch { error = "IMAGE_CONVERSION_FAILED"; }
        File.WriteAllText(Path.Combine(job.OutputDirectory, "result.json"), JsonSerializer.Serialize(new { image, error }));
        return 0;
    }
    public static ConvertedImage Convert(ImageJob job)
    {
        job.Policy.Validate();
        var length = new FileInfo(job.InputPath).Length;
        if (length is < 1 || length > job.MaxBytes) throw new ImageProblem("IMAGE_SIZE_LIMIT");
        var bytes = File.ReadAllBytes(job.InputPath);
        ValidateContainer(bytes);
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data) ?? throw new ImageProblem("IMAGE_INVALID");
        var media = codec.EncodedFormat switch { SKEncodedImageFormat.Jpeg => "image/jpeg", SKEncodedImageFormat.Png => "image/png", SKEncodedImageFormat.Webp => "image/webp", _ => throw new ImageProblem("IMAGE_UNSUPPORTED_FORMAT") };
        if (codec.FrameCount > 1) throw new ImageProblem("IMAGE_ANIMATED");
        var info = codec.Info;
        if (info.Width < 1 || info.Height < 1 || info.Width > job.MaxEdge || info.Height > job.MaxEdge || (long)info.Width * info.Height > job.MaxPixels) throw new ImageProblem("IMAGE_PIXEL_LIMIT");
        using var srgb = SKColorSpace.CreateSrgb();
        using var decoded = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Bgra8888, SKAlphaType.Premul, srgb));
        if (codec.GetPixels(decoded.Info, decoded.GetPixels()) != SKCodecResult.Success) throw new ImageProblem("IMAGE_DECODE_FAILED");
        var swapped = (int)codec.EncodedOrigin >= 5;
        var width = swapped ? info.Height : info.Width; var height = swapped ? info.Width : info.Height;
        // 原寸の向き補正用コピーを作らず、表示・サムネイルの大きさへ直接描画する。
        var display = Encode(decoded, codec.EncodedOrigin, width, height, job.Policy.DisplayMaxEdge, job.Policy.DisplayQuality, Path.Combine(job.OutputDirectory, "display.webp"));
        var thumbnail = Encode(decoded, codec.EncodedOrigin, width, height, job.Policy.ThumbMaxEdge, job.Policy.ThumbQuality, Path.Combine(job.OutputDirectory, "thumbnail.webp"));
        return new(media, width, height, display.Width, display.Height, thumbnail.Width, thumbnail.Height, info.Width, info.Height);
    }
    private static SKSizeI Encode(SKBitmap original, SKEncodedOrigin origin, int width, int height, int edge, int quality, string path)
    {
        var ratio = Math.Min(1, (double)edge / Math.Max(width, height));
        var size = new SKSizeI(Math.Max(1, (int)Math.Round(width * ratio)), Math.Max(1, (int)Math.Round(height * ratio)));
        using var scaled = new SKBitmap(new SKImageInfo(size.Width, size.Height, original.ColorType, original.AlphaType, original.ColorSpace));
        using (var canvas = new SKCanvas(scaled))
        {
            canvas.Clear(SKColors.Transparent);
            var matrix = OriginMatrix(origin, original.Width, original.Height);
            var sx = (float)size.Width / width; var sy = (float)size.Height / height;
            matrix.ScaleX *= sx; matrix.SkewX *= sx; matrix.TransX *= sx;
            matrix.SkewY *= sy; matrix.ScaleY *= sy; matrix.TransY *= sy;
            canvas.SetMatrix(matrix); canvas.DrawBitmap(original, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None)); canvas.Flush();
        }
        using var pixmap = scaled.PeekPixels();
        using var encoded = pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, quality)) ?? throw new ImageProblem("IMAGE_CONVERSION_FAILED");
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); encoded.SaveTo(stream); stream.Flush(true);
        return size;
    }
    public static SKMatrix OriginMatrix(SKEncodedOrigin origin, int width, int height) => origin switch
    {
        SKEncodedOrigin.TopRight => Matrix(-1, 0, width, 0, 1, 0),
        SKEncodedOrigin.BottomRight => Matrix(-1, 0, width, 0, -1, height),
        SKEncodedOrigin.BottomLeft => Matrix(1, 0, 0, 0, -1, height),
        SKEncodedOrigin.LeftTop => Matrix(0, 1, 0, 1, 0, 0),
        SKEncodedOrigin.RightTop => Matrix(0, -1, height, 1, 0, 0),
        SKEncodedOrigin.RightBottom => Matrix(0, -1, height, -1, 0, width),
        SKEncodedOrigin.LeftBottom => Matrix(0, 1, 0, -1, 0, width),
        _ => SKMatrix.Identity
    };
    private static SKMatrix Matrix(float a, float b, float c, float d, float e, float f) => new() { ScaleX = a, SkewX = b, TransX = c, SkewY = d, ScaleY = e, TransY = f, Persp2 = 1 };
    public static void ValidateContainer(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            var offset = 8; var ended = false;
            while (offset <= bytes.Length - 12)
            {
                var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));
                if (length > bytes.Length - offset - 12) throw new ImageProblem("IMAGE_INVALID");
                var type = bytes.Slice(offset + 4, 4);
                if (type.SequenceEqual("acTL"u8) || type.SequenceEqual("fcTL"u8) || type.SequenceEqual("fdAT"u8)) throw new ImageProblem("IMAGE_ANIMATED");
                offset += checked((int)length + 12);
                if (type.SequenceEqual("IEND"u8)) { ended = offset == bytes.Length; break; }
            }
            if (!ended) throw new ImageProblem("IMAGE_INVALID");
            return;
        }
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4, 4)) != bytes.Length - 8) throw new ImageProblem("IMAGE_INVALID");
            var offset = 12;
            while (offset <= bytes.Length - 8)
            {
                var type = bytes.Slice(offset, 4); var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset + 4, 4));
                if (length > bytes.Length - offset - 8) throw new ImageProblem("IMAGE_INVALID");
                if (type.SequenceEqual("ANIM"u8) || type.SequenceEqual("ANMF"u8) || (type.SequenceEqual("VP8X"u8) && length >= 1 && (bytes[offset + 8] & 2) != 0)) throw new ImageProblem("IMAGE_ANIMATED");
                offset += checked(8 + (int)length + (int)(length & 1));
            }
            if (offset != bytes.Length) throw new ImageProblem("IMAGE_INVALID");
            return;
        }
        if (bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255) return;
        throw new ImageProblem("IMAGE_UNSUPPORTED_FORMAT");
    }
}
