using System.Data.Common;
using System.Net.Http.Json;
using System.Text.Json;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using static GoodsManager.Tests.AuthenticationTests;

namespace GoodsManager.Tests;

public sealed class MasterSnapshotTests
{
    [Test]
    public async Task RelationPageItemsOwnerVersionAndDataRevisionShareOneSnapshot()
    {
        var pause = new PauseAfterCount("Series");
        await using var app = new TestApplication { ConfigureTestServices = services => services.AddDbContext<AppDbContext>(options => options.AddInterceptors(pause)) };
        using var client = Client(app);
        await SetupAsync(client);
        var workId = Guid.NewGuid();
        var seriesId = Guid.NewGuid();
        long before;
        await using (var initial = app.Services.CreateAsyncScope())
        {
            var db = initial.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Add(new Work { Id = workId, Name = "w", NormalizedName = "w" });
            db.Add(new Series { Id = seriesId, Name = "s", NormalizedName = "s" });
            db.Add(new WorkSeries { WorkId = workId, SeriesId = seriesId });
            (await db.SystemStates.SingleAsync()).DataRevision++;
            await db.SaveChangesAsync();
            before = (await db.SystemStates.SingleAsync()).DataRevision;
        }
        var pending = client.GetAsync($"/api/v1/masters/works/{workId:D}/relations");
        try
        {
            await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using var writer = app.Services.CreateAsyncScope();
            var db = writer.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var addedId = Guid.NewGuid();
            db.Add(new Series { Id = addedId, Name = "a new", NormalizedName = "a new" });
            db.Add(new WorkSeries { WorkId = workId, SeriesId = addedId });
            (await db.Set<Work>().SingleAsync(x => x.Id == workId)).Revision++;
            (await db.SystemStates.SingleAsync()).DataRevision++;
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        finally { pause.Release.TrySetResult(); }
        var response = await pending;
        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(page.GetProperty("totalCount").GetInt64(), Is.EqualTo(1));
        Assert.That(page.GetProperty("items").GetArrayLength(), Is.EqualTo(1));
        Assert.That(page.GetProperty("items")[0].GetProperty("id").GetGuid(), Is.EqualTo(seriesId));
        Assert.That(page.GetProperty("masterRevision").GetString(), Is.EqualTo("1"));
        Assert.That(response.Headers.ETag!.ToString(), Is.EqualTo(page.GetProperty("masterEtag").GetString()));
        Assert.That(page.GetProperty("dataRevision").GetString(), Is.EqualTo(before.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Test]
    public async Task CountItemsAndDataRevisionUseOneSnapshotWhileAnotherConnectionWrites()
    {
        var pause = new PauseAfterCount();
        await using var app = new TestApplication
        {
            ConfigureTestServices = services => services.AddDbContext<AppDbContext>(options => options.AddInterceptors(pause))
        };
        using var client = Client(app);
        await SetupAsync(client);
        long before;
        Guid originalId;
        await using (var initial = app.Services.CreateAsyncScope())
        {
            var db = initial.ServiceProvider.GetRequiredService<AppDbContext>();
            var tag = new Tag { Id = Guid.NewGuid(), Name = "z original", NormalizedName = "z original" };
            originalId = tag.Id;
            db.Add(tag);
            var state = await db.SystemStates.SingleAsync();
            state.DataRevision++;
            before = state.DataRevision;
            await db.SaveChangesAsync();
        }
        var pending = client.GetFromJsonAsync<JsonElement>("/api/v1/masters/tags");
        try
        {
            await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using var writer = app.Services.CreateAsyncScope();
            var db = writer.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.Add(new Tag { Id = Guid.NewGuid(), Name = "a new", NormalizedName = "a new" });
            (await db.SystemStates.SingleAsync()).DataRevision++;
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        finally { pause.Release.TrySetResult(); }
        var page = await pending;
        Assert.That(page.GetProperty("totalCount").GetInt64(), Is.EqualTo(1));
        Assert.That(page.GetProperty("items").GetArrayLength(), Is.EqualTo(1));
        Assert.That(page.GetProperty("items")[0].GetProperty("id").GetGuid(), Is.EqualTo(originalId));
        Assert.That(page.GetProperty("dataRevision").GetString(), Is.EqualTo(before.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var after = await client.GetFromJsonAsync<JsonElement>("/api/v1/masters/tags");
        Assert.That(after.GetProperty("totalCount").GetInt64(), Is.EqualTo(2));
    }

    private sealed class PauseAfterCount(string table = "Tags") : DbCommandInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int paused;
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("COUNT(*)", StringComparison.Ordinal) && command.CommandText.Contains($"FROM \"{table}\"", StringComparison.Ordinal) && Interlocked.Exchange(ref paused, 1) == 0)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
