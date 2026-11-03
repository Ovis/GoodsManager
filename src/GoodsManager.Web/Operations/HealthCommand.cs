namespace GoodsManager.Web.Operations;

/// <summary>シェルやcurlを実行イメージへ追加せず、内部HTTPで準備状態を確認</summary>
public static class HealthCommand
{
    /// <summary>内部専用リスナーの準備応答が成功した場合だけ終了コード0</summary>
    public static async Task<int> RunAsync(IConfiguration configuration)
    {
        try
        {
            var port = configuration.GetValue("Operations:HealthPort", 8081);
            if (port is < 1 or > 65535)
            {
                return 1;
            }

            using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(5) };

            return (await client.GetAsync($"http://127.0.0.1:{port}/health/ready")).IsSuccessStatusCode ? 0 : 1;
        }
        catch (HttpRequestException) { return 1; }
        catch (TaskCanceledException) { return 1; }
    }
}
