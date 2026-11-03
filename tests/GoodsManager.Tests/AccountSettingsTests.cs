using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static GoodsManager.Tests.AuthenticationTests;

namespace GoodsManager.Tests;

public sealed class AccountSettingsTests
{
    [Test]
    public async Task LoginIdChangeKeepsSessionsAndPasswordAndRejectsOldEtag()
    {
        await using var app = new TestApplication(); using var first = Client(app); await SetupAsync(first);
        using var second = Client(app); await SendAsync(second, "auth/password", new { loginId = "owner", password = Password });
        var original = await first.GetFromJsonAsync<JsonElement>("/api/v1/auth/session");
        var etag = original.GetProperty("user").GetProperty("etag").GetString();
        Assert.That((await SendAsync(first, "account", new { loginId = "new.owner" }, HttpMethod.Put, etag)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        foreach (var client in new[] { first, second })
        {
            var session = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/session");
            Assert.That(session.GetProperty("user").GetProperty("loginId").GetString(), Is.EqualTo("new.owner"));
        }
        Assert.That((await SendAsync(first, "account", new { loginId = "stale" }, HttpMethod.Put, etag)).StatusCode, Is.EqualTo(HttpStatusCode.PreconditionFailed));
        using var next = Client(app);
        Assert.That((await SendAsync(next, "auth/password", new { loginId = "new.owner", password = Password })).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
