using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoodsManager.Web.Auth;
using GoodsManager.Web.Collections;
using GoodsManager.Web.PhotoOcr;
using GoodsManager.Web.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static GoodsManager.Tests.AuthenticationTests;

namespace GoodsManager.Tests;

public sealed class PhotoOcrTests
{
    [Test]
    public async Task EditorCanUseRegistrationAssetsWithoutAdministratorRightsButEveryRequestChecksMembershipAndAccount()
    {
        var directory = Path.Combine(Path.GetTempPath(), "GoodsManager.Tests", Guid.NewGuid().ToString("N"));
        var assets = new PhotoOcrAssets(Path.Combine(directory, "ocr"));
        foreach (var path in new[] { "workers/ocr.worker.js", "assets/engine.js", "models/PP-OCRv5_mobile_det_onnx_infer.tar", "models/PP-OCRv5_mobile_rec_onnx_infer.tar", "runtime/ort/ort-wasm-simd-threaded.wasm", "notices/index.txt" })
        {
            var target = Path.Combine(assets.DirectoryPath, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllTextAsync(target, "test asset");
        }

        await using var app = new TestApplication(directory) { ConfigureTestServices = services => { services.RemoveAll<PhotoOcrAssets>(); services.AddSingleton(assets); } };
        using var owner = Client(app);
        await SetupAsync(owner);

        var editor = new AppUser { Id = Guid.NewGuid(), UserName = "ocr-editor", LockoutEnabled = true };
        await using (var scope = app.Services.CreateAsyncScope())
        {
            Assert.That((await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().CreateAsync(editor, Password)).Succeeded, Is.True);

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Add(new CollectionMember { CollectionId = CollectionSchema.DefaultId, UserId = editor.Id, Role = "editor" });
            await db.SaveChangesAsync();
        }

        using var client = Client(app);
        await SendAsync(client, "auth/password", new
        {
            loginId = editor.UserName,
            password = Password
        });

        var access = await client.GetFromJsonAsync<JsonElement>("/api/v1/registration/photo-ocr");

        Assert.That(access.GetProperty("available").GetBoolean(), Is.True);

        var assetBase = access.GetProperty("assetBase").GetString()!;

        Assert.That(assetBase, Is.EqualTo($"/registration-photo-ocr/{editor.Id}/{CollectionSchema.DefaultId}/"));

        using var worker = await client.GetAsync(assetBase + "workers/ocr.worker.js");

        Assert.That(worker.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(worker.Headers.CacheControl?.NoStore, Is.True);
        Assert.That(worker.Headers.GetValues("Content-Security-Policy").Single(), Does.Contain("'unsafe-eval'"));

        using var engine = await client.GetAsync(assetBase + "assets/engine.js");

        Assert.That(engine.Headers.GetValues("Content-Security-Policy").Single(), Does.Not.Contain("'unsafe-eval'"));
        Assert.That((await client.GetAsync(assetBase + "notices/index.txt")).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var page = await client.GetAsync("/register");

        Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(page.Headers.GetValues("Content-Security-Policy").Single(), Does.Not.Contain("'unsafe-eval'"));
        Assert.That((await client.GetAsync($"/registration-photo-ocr/{Guid.NewGuid()}/{CollectionSchema.DefaultId}/workers/ocr.worker.js")).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That((await client.GetAsync($"/registration-photo-ocr/{editor.Id}/{Guid.NewGuid()}/workers/ocr.worker.js")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        using var anonymous = Client(app);
        using var anonymousAsset = await anonymous.GetAsync(assetBase + "workers/ocr.worker.js");

        Assert.That(anonymousAsset.StatusCode, Is.Not.EqualTo(HttpStatusCode.OK));
        Assert.That(anonymousAsset.Headers.CacheControl?.NoStore, Is.True);
        Assert.That((await anonymous.GetAsync("/api/v1/registration/photo-ocr")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        client.DefaultRequestHeaders.Add("X-Account-Id", Guid.NewGuid().ToString());

        Assert.That((await client.GetAsync("/api/v1/registration/photo-ocr")).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));

        client.DefaultRequestHeaders.Remove("X-Account-Id");
        await using (var scope = app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<CollectionMember>().Where(x => x.UserId == editor.Id).ExecuteDeleteAsync();
        }

        Assert.That((await client.GetAsync("/api/v1/registration/photo-ocr")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.GetAsync(assetBase + "models/PP-OCRv5_mobile_det_onnx_infer.tar")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public void MissingAssetsAreUnavailableAndPathsCannotEscapeAssetDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "GoodsManager.Tests", Guid.NewGuid().ToString("N"));
        var assets = new PhotoOcrAssets(directory);

        Assert.That(assets.Available, Is.False);

        foreach (var path in new[] { "../secret", "models/../../secret", "models\\..\\secret", "/secret", "models//secret", "models/./secret", "C:/secret", "private.json", "models/\0" })
        {
            Assert.That(assets.Resolve(path), Is.Null);
        }
    }
}
