using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using GoodsManager.Web.Masters;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web;

/// <summary>内部例外の内容を公開せず、共通のエラー応答へ変換</summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>秘密や物理パスを含まない応答を返す</summary>
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var sqlite = exception as SqliteException ?? (exception as DbUpdateException)?.InnerException as SqliteException;
        var failure = exception as MasterProblem;
        if (exception is BadHttpRequestException badRequest)
        {
            failure = new MasterProblem(badRequest.StatusCode, badRequest.StatusCode == 413 ? "REQUEST_TOO_LARGE" : "VALIDATION_FAILED");
        }
        if (sqlite?.SqliteErrorCode is 5 or 6)
        {
            context.Response.Headers.RetryAfter = "1";
            failure = new MasterProblem(503, "DATABASE_BUSY");
        }
        if (exception is DbUpdateConcurrencyException) failure = new MasterProblem(412, "REVISION_MISMATCH");
        if (failure is not null)
        {
            var extensions = new Dictionary<string, object?> { ["code"] = failure.Code, ["traceId"] = context.TraceIdentifier };
            if (failure.Field is not null) extensions["errors"] = new Dictionary<string, string[]> { [failure.Field] = ["入力の型・文字数・値を確認してください"] };
            await Results.Problem(statusCode: failure.Status, title: "要求を確認してください", extensions: extensions).ExecuteAsync(context);
            return true;
        }

        if (exception is AuthenticationFailureException)
        {
            await Results.Problem(statusCode: 401, title: "認証セッションが失効", extensions: new Dictionary<string, object?> { ["code"] = "SESSION_EXPIRED", ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context);
            return true;
        }

        // 例外本文や要求本文には秘密が含まれ得るため、照合情報だけを残す
        logger.LogError("処理失敗 code=INTERNAL_ERROR traceId={TraceId} exceptionType={ExceptionType}", context.TraceIdentifier, exception.GetType().Name);
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = 500,
                Title = "処理を完了できなかった",
                Extensions = { ["code"] = "INTERNAL_ERROR" }
            }
        });
    }
}
