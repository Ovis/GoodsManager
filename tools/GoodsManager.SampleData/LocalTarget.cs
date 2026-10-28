using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace GoodsManager.SampleData;

public static class LocalTarget
{
    /// <summary>資格情報を読む前に、接続先がHTTPSのオリジンであることを確認する。</summary>
    public static Uri Parse(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || uri.Host.Length == 0 || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("接続先は https://example.com のような、パス・クエリ・資格情報を含まないHTTPSのオリジンを指定してください。");
        return uri;
    }

    /// <summary>通常の証明書検証を使う。明示した公開証明書がある場合だけ、名前一致とその証明書を照合する。</summary>
    public static HttpClient CreateClient(Uri origin, string? certificatePath)
    {
        Parse(origin.AbsoluteUri);
        var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false };
        if (certificatePath is not null)
        {
            using var pinned = X509CertificateLoader.LoadCertificateFromFile(certificatePath);
            var raw = pinned.RawData;
            handler.ServerCertificateCustomValidationCallback = (_, certificate, _, errors) => certificate is not null
                && (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) == SslPolicyErrors.None
                && certificate.RawData.SequenceEqual(raw) && DateTime.UtcNow >= certificate.NotBefore.ToUniversalTime()
                && DateTime.UtcNow <= certificate.NotAfter.ToUniversalTime();
        }
        return new HttpClient(handler) { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(60) };
    }
}
