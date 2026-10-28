using GoodsManager.Web.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Auth;

/// <summary>停止中CLIのパスワード再設定。HTTPへは公開しない</summary>
public sealed class AccountRecovery(AppDbContext db, UserManager<AppUser> users, TimeProvider clock)
{
    /// <summary>パスキーの保持・削除を明示して、パスワード・全端末失効を一括確定</summary>
    public async Task<IdentityResult> ResetAsync(string password, bool clearPasskeys, string? loginId = null)
    {
        AppUser? user;
        if (loginId is not null) user = await users.FindByNameAsync(loginId);
        else
        {
            var candidates = await db.Users.Take(2).ToArrayAsync();
            user = candidates.Length == 1 ? candidates[0] : null;
        }
        if (user is null) return IdentityResult.Failed(new IdentityError { Code = "LOGIN_ID_REQUIRED", Description = "対象のログインIDを指定してください。" });
        await using var transaction = await db.Database.BeginTransactionAsync();
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, token, password);
        if (!result.Succeeded)
        {
            return result;
        }

        if (clearPasskeys)
        {
            foreach (var passkey in await users.GetPasskeysAsync(user))
            {
                AuthController.EnsureSuccess(await users.RemovePasskeyAsync(user, passkey.CredentialId));
            }
        }

        user.LockoutEnd = null;
        user.AccessFailedCount = 0;
        AuthController.EnsureSuccess(await users.UpdateAsync(user));
        await db.AuthTickets.Where(x => x.UserId == user.Id).ExecuteDeleteAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE SystemState SET DataRevision=DataRevision+1, UpdatedAt={StorageValues.ToMilliseconds(clock.GetUtcNow())} WHERE Id=1");
        await transaction.CommitAsync();
        return result;
    }

    /// <summary>秘密を引数・環境変数・標準出力へ残さず対話入力を受ける</summary>
    public static async Task<int> RunCliAsync(IServiceProvider services, string[] args)
    {
        if (Console.IsInputRedirected || args.Length != 2 || args[1] is not ("--keep-passkeys" or "--clear-passkeys"))
        {
            Console.Error.WriteLine("停止中の対話端末で recover-account --keep-passkeys または --clear-passkeys を指定してください");
            return 2;
        }

        Console.Write("復旧するログインID: ");
        var loginId = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(loginId)) return 2;
        Console.Write("新しいパスワード: ");
        var password = ReadSecret();
        Console.Write("新しいパスワード（再入力）: ");
        if (password != ReadSecret())
        {
            Console.Error.WriteLine("再入力が一致しない");
            return 2;
        }

        await using var scope = services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<AccountRecovery>().ResetAsync(password, args[1] == "--clear-passkeys", loginId);
        Console.WriteLine(result.Succeeded ? "パスワードを再設定し、全端末を失効" : "パスワード条件を満たしていないため変更を取消");
        return result.Succeeded ? 0 : 1;
    }

    /// <summary>端末のエコーを止めてパスワードを読む</summary>
    private static string ReadSecret()
    {
        var text = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return text.ToString();
            }

            if (key.Key == ConsoleKey.Backspace && text.Length > 0)
            {
                text.Length--;
            }
            else if (!char.IsControl(key.KeyChar))
            {
                text.Append(key.KeyChar);
            }
        }
    }
}
