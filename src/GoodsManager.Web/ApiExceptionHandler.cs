using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;

namespace GoodsManager.Web;

/// <summary>内部例外の内容を公開せず、共通のエラー応答へ変換</summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>秘密や物理パスを含まない応答を返す</summary>
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
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
