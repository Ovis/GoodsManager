using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GoodsManager.Web.Backups;
using GoodsManager.Web.Storage;

namespace GoodsManager.Web.Operations;

/// <summary>本文・秘密・SQLを保存せず、固定コードと照合情報だけを記録</summary>
public sealed partial class OperationalLogging : ILoggerProvider
{
    private readonly object gate = new();
    private readonly string directory;
    private readonly TimeProvider clock;
    private readonly TextWriter output;
    private readonly long maximumBytes;
    private DateOnly? cleanedDate;
    public bool FileFailed { get; private set; }
    public string DirectoryPath => directory;
    [GeneratedRegex("^goodsmanager-([0-9]{8})-[0-9]{6}\\.jsonl$")] private static partial Regex FilePattern();
    [GeneratedRegex("^[A-Z][A-Z0-9_]{0,100}$")] private static partial Regex CodePattern();

    /// <summary>既定は1ファイル50MB、UTC日次・30暦日保持</summary>
    public OperationalLogging(IConfiguration configuration, DatabaseRuntime runtime)
        : this(configuration["Operations:LogDirectory"] ?? Path.Combine(runtime.DirectoryPath, "logs"), TimeProvider.System, Console.Out) { }

    /// <summary>日付・容量境界を実ファイルで検証できる構成</summary>
    public OperationalLogging(string directory, TimeProvider clock, TextWriter output, long maximumBytes = 50_000_000)
    {
        this.directory = Path.GetFullPath(directory); this.clock = clock; this.output = output; this.maximumBytes = maximumBytes;
        BackupPaths.RejectLinks(this.directory); Directory.CreateDirectory(this.directory);
    }

    /// <summary>標準フレームワークの自由文字列も記録対象から除外</summary>
    public ILogger CreateLogger(string categoryName) => new SafeLogger(this, categoryName);
    public void Dispose() { }

    /// <summary>全行をJSONにし、例外本文や構造化引数の自由文字列を捨てる</summary>
    private void Write<TState>(string category, LogLevel level, EventId eventId, TState state, Exception? exception)
    {
        var own = category.StartsWith("GoodsManager.", StringComparison.Ordinal);
        if (level < (own ? LogLevel.Information : LogLevel.Warning)) return;
        var fields = new Dictionary<string, object?>
        {
            ["timestamp"] = clock.GetUtcNow(),
            ["level"] = level.ToString(),
            ["category"] = category,
            ["eventId"] = eventId.Id,
            ["traceId"] = Activity.Current?.TraceId.ToString(),
            ["exceptionType"] = exception?.GetType().Name
        };
        if (own && state is IEnumerable<KeyValuePair<string, object?>> values)
            foreach (var pair in values)
            {
                // テンプレート自体も動的入力の可能性があるため、メッセージは保存しない。
                if (pair.Key is "Id" or "UploadId" or "FileId" or "RunId" or "OperationId" && pair.Value is Guid id) fields[pair.Key] = id;
                else if (pair.Key is "Code" or "ErrorCode" && pair.Value is string code && CodePattern().IsMatch(code)) fields["errorCode"] = code;
                else if (pair.Key is "TraceId" or "ExceptionType" or "Type" && pair.Value is string value && value.Length <= 128 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is ':' or '-' or '_')) fields[pair.Key] = value;
            }
        var line = JsonSerializer.Serialize(fields) + "\n";
        lock (gate)
        {
            output.Write(line); output.Flush();
            try
            {
                Append(line); FileFailed = false;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                FileFailed = true;
                output.WriteLine("{\"level\":\"Error\",\"errorCode\":\"LOG_STORAGE_FAILED\"}"); output.Flush();
            }
        }
    }

    /// <summary>日付で保持期間を判定し、対象外ファイルやリンクを削除しない</summary>
    private void Append(string line)
    {
        BackupPaths.RejectLinks(directory);
        var date = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (cleanedDate != date)
        {
            foreach (var file in Directory.EnumerateFiles(directory, "goodsmanager-*.jsonl"))
            {
                var match = FilePattern().Match(Path.GetFileName(file));
                if (match.Success && DateOnly.TryParseExact(match.Groups[1].Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var stored) && stored < date.AddDays(-29))
                { BackupPaths.RejectLinks(file); File.Delete(file); }
            }
            cleanedDate = date;
        }
        var bytes = Encoding.UTF8.GetBytes(line);
        for (var sequence = 0; sequence < 1_000_000; sequence++)
        {
            var file = Path.Combine(directory, $"goodsmanager-{date:yyyyMMdd}-{sequence:000000}.jsonl"); BackupPaths.RejectLinks(file);
            if (File.Exists(file) && new FileInfo(file).Length + bytes.Length > maximumBytes) continue;
            using var stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.Read);
            stream.Write(bytes); stream.Flush(true); return;
        }
        throw new IOException("ログ分割数の上限");
    }

    private sealed class SafeLogger(OperationalLogging owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= (category.StartsWith("GoodsManager.", StringComparison.Ordinal) ? LogLevel.Information : LogLevel.Warning);
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { if (IsEnabled(level)) owner.Write(category, level, eventId, state, exception); }
    }
}
