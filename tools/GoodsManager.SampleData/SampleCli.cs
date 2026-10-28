using System.Text;

namespace GoodsManager.SampleData;

public static class SampleCli
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Contains("--help")) { Help(); return 0; }
            var values = new Dictionary<string, string>(); var passwordStdin = false;
            for (var index = 0; index < args.Length; index++)
            {
                if (args[index] == "--password-stdin") { passwordStdin = true; continue; }
                if (args[index] is not ("--url" or "--login" or "--state-directory" or "--certificate" or "--collection") || index + 1 >= args.Length || !values.TryAdd(args[index], args[++index]))
                    throw new ArgumentException("引数を確認してください。--help で使用方法を表示します。");
            }
            var origin = LocalTarget.Parse(values.GetValueOrDefault("--url", "https://localhost:7449"));
            using var client = LocalTarget.CreateClient(origin, values.GetValueOrDefault("--certificate"));
            var api = new SampleApi(client);
            if ((await api.ReadAsync("setup/status")).GetProperty("setupRequired").GetBoolean())
                throw new InvalidOperationException("先にブラウザで初期登録を完了してください: " + origin);
            var login = values.GetValueOrDefault("--login");
            if (string.IsNullOrEmpty(login)) { Console.Write("ログインID: "); login = Console.ReadLine(); }
            if (string.IsNullOrEmpty(login)) throw new ArgumentException("ログインIDを入力してください。");
            Console.WriteLine("接続先: " + origin + " / ログインID: " + login);
            string password;
            if (passwordStdin) password = Console.ReadLine() ?? throw new ArgumentException("標準入力からパスワードを読み込めません。");
            else
            {
                if (Console.IsInputRedirected) throw new ArgumentException("対話端末を使うか、--password-stdin を指定してください。");
                Console.Write("パスワード（非表示）: "); password = ReadSecret(); Console.WriteLine();
            }
            await api.SendAsync("auth/password", new { loginId = login, password, rememberMe = false });
            password = "";
            try
            {
                var collections = (await api.ReadAsync("collections")).GetProperty("items").EnumerateArray().Select(x => (Id: x.GetProperty("id").GetString()!, Name: x.GetProperty("name").GetString()!)).ToArray();
                var selected = values.GetValueOrDefault("--collection");
                var candidates = collections.Where(x => selected is null || x.Id == selected || x.Name == selected).ToArray();
                if (candidates.Length != 1)
                {
                    if (selected is not null || Console.IsInputRedirected || candidates.Length == 0) throw new ArgumentException("投入先コレクションを --collection 名前またはID で指定してください。");
                    for (var i = 0; i < candidates.Length; i++) Console.WriteLine($"{i + 1}. {candidates[i].Name}");
                    Console.Write("投入先の番号: ");
                    if (!int.TryParse(Console.ReadLine(), out var index) || index < 1 || index > candidates.Length) throw new ArgumentException("投入先を選択してください。");
                    candidates = [candidates[index - 1]];
                }
                client.DefaultRequestHeaders.Add("X-Collection-Id", candidates[0].Id);
                Console.WriteLine("投入先コレクション: " + candidates[0].Name);
                var importer = new SampleDataImporter(client, SampleCatalog.Load(), values.GetValueOrDefault("--state-directory", "artifacts/sample-data"), Console.Out);
                var result = await importer.RunAsync();
                Console.WriteLine($"完了: 追加・再開 {result.Added}件 / 既存のためスキップ {result.Skipped}件");
                Console.WriteLine("ブラウザのコレクションを再読み込みしてください。");
                return 0;
            }
            finally
            {
                try { await api.SendAsync("auth/logout", new { }); }
                catch (HttpRequestException) { Console.Error.WriteLine("投入CLIのログアウトを完了できませんでした。Cookieはファイルに保存していません。"); }
            }
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or HttpRequestException or System.Text.Json.JsonException or OperationCanceledException or TimeoutException)
        {
            Console.Error.WriteLine(error.Message);
            Console.Error.WriteLine("投入を停止しました。既存データは削除していません。接続先・入力・実行記録を確認してください。");
            return 1;
        }
    }

    private static string ReadSecret()
    {
        var text = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) return text.ToString();
            if (key.Key == ConsoleKey.Backspace) { if (text.Length > 0) text.Length--; }
            else if (!char.IsControl(key.KeyChar)) text.Append(key.KeyChar);
        }
    }

    private static void Help() => Console.WriteLine("""
        指定したGoodsManagerにモック由来の64商品と画像・関連マスターを追加します。
        dotnet run --project tools/GoodsManager.SampleData -- [オプション]
          --url HTTPSのURL              接続先オリジン（既定: https://localhost:7449）
          --login ログインID            省略時は入力
          --collection 名前またはID    複数ある場合は投入先を明示（省略時は対話選択）
          --state-directory パス        実行記録（既定: artifacts/sample-data）
          --certificate 公開証明書      必要な場合のみ、接続先証明書を固定照合
          --password-stdin              パスワードを標準入力から1行読む（自動実行用）
        パスワードは引数・環境変数・実行記録には保存しません。
        """);
}
