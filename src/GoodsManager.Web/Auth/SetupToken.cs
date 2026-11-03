using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GoodsManager.Web.Backups;
using GoodsManager.Web.Storage;

namespace GoodsManager.Web.Auth;

/// <summary>初回起動時の管理者登録を永続領域の所有者だけに許可する一時秘密。</summary>
public sealed class SetupToken(DatabaseRuntime runtime, TimeProvider clock, ILogger<SetupToken> logger)
{
    private byte[]? hash;
    private DateTimeOffset expires;

    /// <summary>初期登録を許可する一時秘密の永続ファイル。Web応答には公開しない。</summary>
    private string FilePath => Path.Combine(runtime.DirectoryPath, "setup-token.txt");

    /// <summary>有効な既存値は維持し、期限切れだけ起動時に再発行。破損・読取失敗では起動を拒否する。</summary>
    public void Initialize(bool hasAccounts)
    {
        hash = null;
        if (hasAccounts)
        {
            Invalidate();
            return;
        }

        BackupPaths.RejectLinks(FilePath);
        if (!File.Exists(FilePath))
        {
            Create();
        }

        var stored = Read();
        if (stored.Expires <= clock.GetUtcNow())
        {
            Create();
            stored = Read();
        }

        if (stored.Expires > clock.GetUtcNow().AddHours(24))
        {
            throw new InvalidDataException("初期登録情報の有効期限が不正");
        }

        hash = SHA256.HashData(Encoding.ASCII.GetBytes(stored.Token));
        expires = stored.Expires;
    }

    /// <summary>期限・現在のファイル・起動時の値を照合し、失敗しても有効な秘密を消費しない。</summary>
    public bool Verify(string? supplied)
    {
        if (hash is null || expires <= clock.GetUtcNow() || supplied is not { Length: 64 })
        {
            return false;
        }

        try
        {
            var stored = Read();
            return stored.Expires == expires
                && CryptographicOperations.FixedTimeEquals(hash, SHA256.HashData(Encoding.ASCII.GetBytes(stored.Token)))
                && CryptographicOperations.FixedTimeEquals(hash, SHA256.HashData(Encoding.UTF8.GetBytes(supplied)));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>登録確定後に失効する。削除失敗時もアカウント存在検査とメモリ上の失効を優先する。</summary>
    public void Invalidate()
    {
        hash = null;
        try
        {
            BackupPaths.RejectLinks(FilePath);
            File.Delete(FilePath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            logger.LogWarning("初期登録情報の削除失敗 Code={Code}", "SETUP_TOKEN_CLEANUP_FAILED");
        }
    }

    /// <summary>リンク・ファイル権限・容量・2行の書式を検査し、一時秘密と期限を読む。</summary>
    private (string Token, DateTimeOffset Expires) Read()
    {
        BackupPaths.RejectLinks(FilePath);
        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(FilePath) & ~(UnixFileMode.UserRead | UnixFileMode.UserWrite)) != 0)
        {
            throw new InvalidDataException("初期登録情報の権限が不正");
        }

        using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 256)
        {
            throw new InvalidDataException("初期登録情報が不正");
        }

        using var reader = new StreamReader(stream, Encoding.ASCII);
        var token = reader.ReadLine();
        var deadline = reader.ReadLine();
        if (token is not { Length: 64 } || token.Any(c => !char.IsAsciiHexDigit(c)) || reader.ReadLine() is not null
            || !DateTimeOffset.TryParseExact(deadline, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiry))
        {
            throw new InvalidDataException("初期登録情報が不正");
        }

        return (token, expiry);
    }

    /// <summary>256ビットの秘密と24時間の期限を、一時ファイルからの置換で確定する。</summary>
    private void Create()
    {
        var temporary = Path.Combine(runtime.DirectoryPath, ".setup-token-" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (var stream = new FileStream(temporary, options))
            {
                var content = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)) + "\n" + clock.GetUtcNow().AddHours(24).ToString("O", CultureInfo.InvariantCulture) + "\n";
                stream.Write(Encoding.ASCII.GetBytes(content));
                stream.Flush(true);
            }

            BackupPaths.RejectLinks(FilePath);
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
