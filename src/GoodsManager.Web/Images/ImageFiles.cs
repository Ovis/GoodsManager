using System.Security.Cryptography;
using System.Text.RegularExpressions;
using GoodsManager.Web.Storage;

namespace GoodsManager.Web.Images;

public interface IImageFiles
{
    string Root { get; }
    string NewStagingPath();
    string NewJobDirectory();
    string Resolve(string relativePath);
    Task<string> HashAsync(string path, CancellationToken token);
    Task PublishAsync(string stagedPath, string sha256, CancellationToken token);
    Task VerifyAsync(ImageFile file, CancellationToken token);
    void Delete(string relativePath);
}
public sealed partial class ImageFiles(DatabaseRuntime runtime) : IImageFiles
{
    public string Root { get; } = Path.Combine(runtime.DirectoryPath, "images");
    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)] private static partial Regex HashPattern();
    [GeneratedRegex("^(objects/[0-9a-f]{2}/[0-9a-f]{64}|staging/[0-9a-f]{32}\\.input|jobs/[0-9a-f]{32}/[a-z.]+)$", RegexOptions.CultureInvariant)] private static partial Regex PathPattern();
    public static string ObjectPath(string sha256)
    {
        if (!HashPattern().IsMatch(sha256)) throw new InvalidDataException("画像ハッシュの保存形式が不正");
        return $"objects/{sha256[..2]}/{sha256}";
    }
    public string NewStagingPath() { var relative = $"staging/{Guid.NewGuid():N}.input"; Directory.CreateDirectory(Path.Combine(Root, "staging")); return relative; }
    public string NewJobDirectory() { var path = Path.Combine(Root, "jobs", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    public string Resolve(string relativePath)
    {
        if (!PathPattern().IsMatch(relativePath)) throw new InvalidDataException("画像パスの保存形式が不正");
        var root = Path.GetFullPath(Root) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidDataException("画像パスが領域外");
        var directory = Path.GetDirectoryName(full);
        while (directory is not null && directory.Length >= root.Length - 1)
        {
            if (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("画像領域内のリンクを拒否");
            directory = Path.GetDirectoryName(directory);
        }
        if (File.Exists(full) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("画像ファイルのリンクを拒否");
        return full;
    }
    public async Task<string> HashAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token));
    }
    public async Task PublishAsync(string stagedPath, string sha256, CancellationToken token)
    {
        if (await HashAsync(stagedPath, token) != sha256) throw new InvalidDataException("公開前の画像ハッシュが不一致");
        var destination = Resolve(ObjectPath(sha256)); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination))
        {
            if (await HashAsync(destination, token) != sha256) throw new InvalidDataException("既存画像のハッシュが不一致");
            return;
        }
        // 同じ永続ボリューム内で確定済みファイルを改名し、DB参照はこの後に作る。
        var sibling = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            await using (var input = new FileStream(stagedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (var output = new FileStream(sibling, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { await input.CopyToAsync(output, token); output.Flush(true); }
            File.Move(sibling, destination, overwrite: false);
        }
        finally { File.Delete(sibling); }
    }
    public async Task VerifyAsync(ImageFile file, CancellationToken token)
    {
        if (file.RelativePath != ObjectPath(file.Sha256)) throw new InvalidDataException("画像パスとハッシュが不一致");
        var path = Resolve(file.RelativePath);
        if (!File.Exists(path) || new FileInfo(path).Length != file.ByteLength || await HashAsync(path, token) != file.Sha256) throw new IOException("画像ファイルが消失または破損");
    }
    public void Delete(string relativePath) => File.Delete(Resolve(relativePath));
}
