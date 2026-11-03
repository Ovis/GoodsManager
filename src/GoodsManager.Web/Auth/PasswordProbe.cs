using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace GoodsManager.Web.Auth;

/// <summary>存在しないIDも検証計算を行うための、起動ごとに異なる比較用ハッシュ。</summary>
public sealed class PasswordProbe(IOptions<PasswordHasherOptions> options)
{
    public string Hash { get; } = new PasswordHasher<AppUser>(options).HashPassword(new AppUser(), Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
}
