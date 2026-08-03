namespace KlavLor.Web.Application.HttpResults;

public sealed class HtmxRetargetResult(string target, RazorComponentResult component, string? swapOverride) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.Headers.Append("HX-Retarget", target);

        if (swapOverride != null)
        {
            httpContext.Response.Headers.Append("HX-Reswap", swapOverride);
        }

        await component.ExecuteAsync(httpContext);
    }
}
