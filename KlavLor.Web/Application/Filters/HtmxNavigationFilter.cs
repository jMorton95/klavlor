namespace KlavLor.Web.Application.Filters;

public sealed class HtmxNavigationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        var requestUrl = $"{request.Path.Value}{request.QueryString}";

        // Only push URL for first-page requests; "Show More" pagination (pageNumber > 1)
        // appends content rather than replacing the page, so the URL should not change.
        var isFirstPage = !int.TryParse(request.Query["pageNumber"], out var page) || page <= 1;

        // A "view" switch (e.g. the source page's Kill Log / Collection Log tabs) is an in-page
        // tab change, not a navigation. The client updates the address bar itself via
        // history.replaceState, so the server must NOT also push a history entry — otherwise the
        // page's Back button would step through tab switches instead of returning to the origin.
        var isTabSwitch = request.Query.ContainsKey("view");

        if (requestUrl.StartsWith("/api") && isFirstPage && !isTabSwitch)
        {
            context.HttpContext.Response.Headers.Append("HX-Push-Url", requestUrl.Replace("/api", ""));
        }

        var result = await next(context);
        return result;
    }
}
