using Microsoft.AspNetCore.Components;

namespace KlavLor.Web.Application.Results;

public static class HttpResultExtensions
{
    extension(IResultExtensions _)
    {
        public static HtmxRedirectResult HtmxRedirect(string redirectUrl) => new(redirectUrl);

        public static HtmxRefreshResult HtmxRefresh() => new();

        public static RazorComponentResult Component<T>(object? parameters = null) where T : ComponentBase
            => parameters is null ? new RazorComponentResult<T>() : new RazorComponentResult<T>(parameters);

        public static HtmxRetargetResult HtmxRetargetResult<T>(string target, object? parameters = null, string? swapOverride = null) where T : ComponentBase =>
            new(target, Component<T>(parameters), swapOverride);
    }
}
