using System.Net.Http.Json;
using System.Text.Json;

namespace GoodsManager.SampleData;

/// <summary>通常の認証・CSRF・変更条件を通し、失敗した変更は自動再送しない。</summary>
public sealed class SampleApi(HttpClient client)
{
    public HttpClient Client => client;
    public async Task<JsonElement> ReadAsync(string path)
    {
        using var response = await client.GetAsync("/api/v1/" + path);
        return await ReadResponseAsync(response, "GET " + path);
    }

    public async Task<JsonElement> SendAsync(string path, object body, string method = "POST", string? etag = null, Guid? key = null)
    {
        using var request = await RequestAsync(path, method);
        request.Content = JsonContent.Create(body, options: SampleCatalog.Json);
        if (etag is not null) request.Headers.TryAddWithoutValidation("If-Match", etag);
        if (key.HasValue) request.Headers.Add("Idempotency-Key", key.Value.ToString());
        using var response = await client.SendAsync(request);
        return await ReadResponseAsync(response, method + " " + path);
    }

    public async Task<HttpRequestMessage> RequestAsync(string path, string method)
    {
        var csrf = await ReadAsync("auth/csrf");
        var request = new HttpRequestMessage(new HttpMethod(method), "/api/v1/" + path);
        request.Headers.Add("X-CSRF-Token", csrf.GetProperty("token").GetString());
        return request;
    }

    public static async Task<JsonElement> ReadResponseAsync(HttpResponseMessage response, string operation)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            string? code = null;
            try { using var error = JsonDocument.Parse(text); code = error.RootElement.GetProperty("code").GetString(); }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException) { }
            // 応答本文や送信した資格情報をログに残さない。
            throw new HttpRequestException($"{operation}: HTTP {(int)response.StatusCode} {code}", null, response.StatusCode);
        }
        if (text.Length == 0) return JsonSerializer.SerializeToElement(new { completed = true });
        using var parsed = JsonDocument.Parse(text);
        return parsed.RootElement.Clone();
    }

    public async Task<List<JsonElement>> AllAsync(string path)
    {
        var result = new List<JsonElement>();
        for (var page = 1; ; page++)
        {
            var value = await ReadAsync(path + (path.Contains('?') ? "&" : "?") + $"page={page}&pageSize=100");
            result.AddRange(value.GetProperty("items").EnumerateArray().Select(x => x.Clone()));
            if (page >= value.GetProperty("totalPages").GetInt64()) return result;
        }
    }
}
