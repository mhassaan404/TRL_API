using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TRL_API.Models;

namespace TRL_API.Helpers
{
    // Every API action (except login/refresh/logout) carries one of these, checked against the user's current access
    // (UserAccess) on every request. Several on one action must all pass.

    // Allowed when the user has ANY of the listed permissions (e.g. a page's view, or another page that shows the same
    // data). The Super Admin always passes.
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class RequirePermissionAttribute : Attribute, IAsyncAuthorizationFilter
    {
        public string[] AnyOf { get; }

        public RequirePermissionAttribute(params string[] anyOf) => AnyOf = anyOf;

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var access = await AccessCheck.GetAsync(context);
            if (access != null && !AnyOf.Any(access.Can))
                context.Result = AccessCheck.Forbidden();
        }
    }

    // Any signed-in, active user (e.g. their own password, the company name for printed documents)
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class SignedInUserAttribute : Attribute, IAsyncAuthorizationFilter
    {
        public async Task OnAuthorizationAsync(AuthorizationFilterContext context) => await AccessCheck.GetAsync(context);
    }

    // The Super Admin only (managing users)
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class SuperAdminOnlyAttribute : Attribute, IAsyncAuthorizationFilter
    {
        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var access = await AccessCheck.GetAsync(context);
            if (access != null && !access.IsSuperAdmin)
                context.Result = AccessCheck.Forbidden();
        }
    }

    internal static class AccessCheck
    {
        public const string NoPermission = "You don't have permission to do this. Ask your administrator.";

        // The user's access, or null with context.Result set: 401 when there is no valid session or the user was
        // deactivated / removed (the screen then tries a refresh, which fails, and shows the login page)
        public static async Task<AccessInfo?> GetAsync(AuthorizationFilterContext context)
        {
            if (context.Result != null) return null; // an earlier filter already refused
            if (context.HttpContext.User.Identity?.IsAuthenticated != true)
            {
                context.Result = new UnauthorizedResult();
                return null;
            }
            var access = await context.HttpContext.RequestServices.GetRequiredService<UserAccess>().GetAsync();
            if (access == null)
                context.Result = new UnauthorizedObjectResult(new ApiResponse { IsSuccess = false, ErrorMessage = "Session expired. Please log in again." });
            return access;
        }

        public static ObjectResult Forbidden() =>
            new(new ApiResponse { IsSuccess = false, Message = NoPermission, ErrorMessage = NoPermission }) { StatusCode = StatusCodes.Status403Forbidden };
    }
}
