using System.Security.Claims;
using GoodsManager.Web.Backups;
using GoodsManager.Web.Images;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Storage;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Collections;

public static class CollectionAccess
{
    public static async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        var expected = context.Request.Headers["X-Account-Id"];
        if (context.User.Identity?.IsAuthenticated == true && expected.Count > 0 && (expected.Count != 1 || expected[0] != context.User.FindFirstValue(ClaimTypes.NameIdentifier))) throw new MasterProblem(409, "ACCOUNT_CHANGED");
        if (action is null || action.ControllerTypeInfo.Namespace == "GoodsManager.Web.Auth" || action.ControllerTypeInfo.AsType() == typeof(CollectionsController)) { await next(context); return; }
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) throw new MasterProblem(401, "SESSION_EXPIRED");
        var db = context.RequestServices.GetRequiredService<AppDbContext>();
        if (action.ControllerTypeInfo.AsType() == typeof(BackupsController) || action.ControllerTypeInfo.AsType() == typeof(SystemAlertsController))
        {
            if (!await db.Users.AnyAsync(x => x.Id == userId && x.IsAdministrator, context.RequestAborted)) throw new MasterProblem(403, "ADMINISTRATOR_REQUIRED");
        }
        else
        {
            var values = context.Request.Headers["X-Collection-Id"];
            if (values.Count == 0 && action.ControllerTypeInfo.AsType() == typeof(ImageMediaController)) values = context.Request.Query["collectionId"];
            if (values.Count != 1 || !Guid.TryParseExact(values[0], "D", out var id) || id == Guid.Empty) throw new MasterProblem(400, "COLLECTION_REQUIRED");
            if (!await db.Set<CollectionMember>().AnyAsync(x => x.CollectionId == id && x.UserId == userId && (x.Role == "manager" || x.Role == "editor"), context.RequestAborted)) throw new MasterProblem(404, "COLLECTION_NOT_FOUND");
            db.CollectionId = id;
        }
        await next(context);
    }
}
