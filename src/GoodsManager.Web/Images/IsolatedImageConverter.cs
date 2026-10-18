using System.Diagnostics;
using System.Text.Json;

namespace GoodsManager.Web.Images;

public interface IImageConverter
{
    Task<ConvertedImage> ConvertAsync(ImageJob job, CancellationToken token);
}
public sealed class IsolatedImageConverter(ImageLimits limits) : IImageConverter
{
    public async Task<ConvertedImage> ConvertAsync(ImageJob job, CancellationToken token)
    {
        Directory.CreateDirectory(job.OutputDirectory);
        var path = Path.Combine(job.OutputDirectory, "job.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(job), token);
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        start.ArgumentList.Add(typeof(IsolatedImageConverter).Assembly.Location); start.ArgumentList.Add("image-worker"); start.ArgumentList.Add(path);
        using var process = Process.Start(start) ?? throw new ImageProblem("IMAGE_WORKER_FAILED");
        var discardOutput = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
        var discardError = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
        var watch = Stopwatch.StartNew();
        try
        {
            while (!process.HasExited)
            {
                token.ThrowIfCancellationRequested();
                process.Refresh();
                if (watch.Elapsed > limits.ConversionTimeout) throw new ImageProblem("IMAGE_TIMEOUT");
                try { if (process.WorkingSet64 > limits.WorkerMemoryBytes || process.PrivateMemorySize64 > limits.WorkerMemoryBytes) throw new ImageProblem("IMAGE_MEMORY_LIMIT"); }
                catch (InvalidOperationException) when (process.HasExited) { break; }
                await Task.Delay(TimeSpan.FromMilliseconds(50), token);
            }
            if (process.ExitCode != 0) throw new ImageProblem("IMAGE_WORKER_FAILED");
            var result = Path.Combine(job.OutputDirectory, "result.json");
            if (!File.Exists(result) || new FileInfo(result).Length > 16384) throw new ImageProblem("IMAGE_WORKER_FAILED");
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(result, token));
            if (json.RootElement.GetProperty("error").ValueKind == JsonValueKind.String) throw new ImageProblem(json.RootElement.GetProperty("error").GetString()!);
            return json.RootElement.GetProperty("image").Deserialize<ConvertedImage>() ?? throw new ImageProblem("IMAGE_WORKER_FAILED");
        }
        finally
        {
            if (!process.HasExited) { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } await process.WaitForExitAsync(CancellationToken.None); }
            await Task.WhenAll(discardOutput, discardError);
        }
    }
}
