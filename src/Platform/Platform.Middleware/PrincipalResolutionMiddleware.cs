using Platform.Abstractions.Principal;

namespace Platform.Middleware;

public sealed class PrincipalResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ICurrentPrincipalAccessor principalAccessor, ICurrentPrincipalFactory principalFactory)
    {
        var principal = principalFactory.CreateFromClaimsPrincipal(context.User);
        principalAccessor.Principal = principal;
        await next(context);
    }
}