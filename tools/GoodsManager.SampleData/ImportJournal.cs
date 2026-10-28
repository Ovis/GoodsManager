using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GoodsManager.SampleData;

/// <summary>接続先・アカウント・データ版ごとの実行記録。Cookieやパスワードは保存しない。</summary>
public sealed class ImportJournal : IDisposable
{
    private readonly string directory;
    private readonly FileStream runLock;
    private readonly SampleApi api;
    public ImportJournal(string root, Uri origin, string userId, SampleApi api)
    {
        this.api = api;
        directory = Path.Combine(Path.GetFullPath(root), Hash(origin.GetLeftPart(UriPartial.Authority) + "\n" + userId + "\n" + string.Join(",", api.Client.DefaultRequestHeaders.GetValues("X-Collection-Id")) + "\n" + SampleCatalog.Version));
        Directory.CreateDirectory(directory);
        runLock = new FileStream(Path.Combine(directory, "run.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private static string Hash(string label) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(label)))[..32];
    private string FileFor(string label, string suffix) => Path.Combine(directory, Hash(label) + suffix);
    public bool HasRequest(string label) => File.Exists(FileFor(label, ".request.json"));
    public bool IsComplete(string label) => File.Exists(FileFor(label, ".complete"));
    public Task CompleteAsync(string label) => SaveAsync(FileFor(label, ".complete"), "complete");

    public async Task<JsonElement?> ResponseAsync(string label)
    {
        var path = FileFor(label, ".response.json");
        if (!File.Exists(path)) return null;
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        return document.RootElement.Clone();
    }

    /// <summary>再実行では本文・キー・If-Matchを変えず、7日を過ぎた未確定要求は停止する。</summary>
    public async Task<JsonElement> WriteAsync(string label, string path, object body, string? etag = null, string method = "POST")
    {
        if (await ResponseAsync(label) is { } response) return response;
        var file = FileFor(label, ".request.json");
        RecordedRequest request;
        if (File.Exists(file)) request = JsonSerializer.Deserialize<RecordedRequest>(await File.ReadAllTextAsync(file), SampleCatalog.Json)!;
        else
        {
            request = new(path, method, etag, Guid.NewGuid(), DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(body, SampleCatalog.Json));
            await SaveAsync(file, JsonSerializer.Serialize(request, SampleCatalog.Json));
        }
        if (request.Path != path || request.Method != method) throw new InvalidDataException("実行記録と要求先が一致しません。");
        if (DateTimeOffset.UtcNow - request.CreatedAt >= TimeSpan.FromDays(7))
            throw new InvalidOperationException("7日を過ぎた未確定要求があります。登録状態を確認してから、別の --state-directory で再実行してください。");
        var value = await api.SendAsync(path, request.Body, method, request.Etag, request.Key);
        await SaveAsync(FileFor(label, ".response.json"), JsonSerializer.Serialize(value, SampleCatalog.Json));
        return value;
    }

    /// <summary>送信前にアップロードキーを記録し、通信断後も同じ画像を重複受付させない。</summary>
    public async Task<Guid> UploadAsync(string label, string image)
    {
        var uploadLabel = "upload:" + label;
        var saved = await ResponseAsync(uploadLabel);
        if (saved is null)
        {
            var keyPath = FileFor(uploadLabel, ".upload-key");
            Guid key;
            if (File.Exists(keyPath)) key = Guid.Parse(await File.ReadAllTextAsync(keyPath));
            else { key = Guid.NewGuid(); await SaveAsync(keyPath, key.ToString()); }
            using var request = await api.RequestAsync("temporary-uploads", "POST");
            request.Headers.Add("Client-Upload-Key", key.ToString());
            using var form = new MultipartFormDataContent();
            var content = new StreamContent(SampleCatalog.OpenImage(image));
            content.Headers.ContentType = new("image/png"); form.Add(content, "file", image); request.Content = form;
            using var response = await api.Client.SendAsync(request);
            saved = await SampleApi.ReadResponseAsync(response, "画像アップロード");
            await SaveAsync(FileFor(uploadLabel, ".response.json"), JsonSerializer.Serialize(saved.Value, SampleCatalog.Json));
        }
        var id = saved.Value.GetProperty("id").GetGuid();
        for (var attempt = 0; attempt < 240; attempt++)
        {
            var status = await api.ReadAsync($"temporary-uploads/{id}");
            var state = status.GetProperty("state").GetString();
            if (state is "ready" or "consumed") return id;
            if (state is not ("pending" or "processing")) throw new InvalidOperationException($"画像の状態が {state} です。期限切れの場合は登録状態を確認し、別の --state-directory で再実行してください。");
            await Task.Delay(250);
        }
        throw new TimeoutException("画像変換を完了できませんでした。サーバーの状態を確認して再実行してください。");
    }

    private static async Task SaveAsync(string path, string text)
    {
        await File.WriteAllTextAsync(path + ".new", text, new UTF8Encoding(false));
        File.Move(path + ".new", path, overwrite: true);
    }
    public void Dispose() => runLock.Dispose();
    private sealed record RecordedRequest(string Path, string Method, string? Etag, Guid Key, DateTimeOffset CreatedAt, JsonElement Body);
}
