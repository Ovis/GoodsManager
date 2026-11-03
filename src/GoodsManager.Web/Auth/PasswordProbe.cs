using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace GoodsManager.Web.Auth;

/// <summary>存在しないIDも検証計算を行うための、起動ごとに異なる比較用ハッシュ。</summary>
public sealed class PasswordProbe(IOptions<PasswordHasherOptions> options)
{
    /// <summary>存在しないIDへの要求でも標準のパスワード検証計算を実行するための比較値。</summary>
    public string Hash { get; } = new PasswordHasher<AppUser>(options).HashPassword(new AppUser(), Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
}
