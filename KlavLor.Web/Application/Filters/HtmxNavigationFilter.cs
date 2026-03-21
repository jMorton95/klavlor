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

        if (requestUrl.StartsWith("/api") && isFirstPage)
        {
            context.HttpContext.Response.Headers.Append("HX-Push-Url", requestUrl.Replace("/api", ""));
        }

        var result = await next(context);
        return result;
    }
}
