using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace GoodsManager.Web;

/// <summary>内部例外の内容を公開せず、共通のエラー応答へ変換</summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    /// <summary>秘密や物理パスを含まない応答を返す</summary>
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
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
