using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.ResponseCompression;
using KlavLor.Application.Common.Settings;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Interfaces.Services;
using KlavLor.Domain.Shared;
using KlavLor.Infrastructure.Persistence.EntityFramework.Interceptors;
using KlavLor.Infrastructure.Services;
using KlavLor.Web.Authentication;

namespace KlavLor.Web.Configuration;

public static class ConfigureApplication
{
    public static IServiceCollection AddConfigurationOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<SystemConfiguration>(
            configuration.GetSection(SettingsRegionConstants.SystemConfiguration));

        services.Configure<DatabaseSettings>(
            configuration.GetSection(SettingsRegionConstants.DatabaseSettings));

        return services;
    }

    extension(WebApplicationBuilder builder)
    {
        public void AddAuditInterceptors()
        {
            builder.Services.AddScoped<IAuditInterceptor, UserIdAuditInterceptor>();
        }

        public void ConfigureAntiForgeryTokens()
        {
            builder.Services.AddAntiforgery(options =>
            {
                options.Cookie.Name = "KlavLor.Web";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            });
        }

        public void ConfigureResponseCompression()
        {
            builder.Services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;
                options.Providers.Add<GzipCompressionProvider>();
                options.Providers.Add<BrotliCompressionProvider>();
            });
        }

        public void ConfigureAuthenticationCookies()
        {
            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                    ApiKeyAuthenticationHandler.SchemeName, null)
                .AddCookie(options =>
                {
                    options.Cookie.Name = "KlavLor.Web.Auth";
                    options.Cookie.HttpOnly = true;
                    options.Cookie.IsEssential = true;
                    options.Cookie.SameSite = SameSiteMode.Strict;
                    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                    options.ExpireTimeSpan = AuthConstants.SessionLifetime;
                    options.SlidingExpiration = true;
                    options.LoginPath = "/login";
                    options.AccessDeniedPath = "/login";
                    // Re-check the principal against the DB on an interval so long-lived sessions
                    // remain revocable. Resolved from the ambient request scope (not a new scope),
                    // which is the supported pattern for cookie auth events.
                    options.Events.OnValidatePrincipal = context =>
                        context.HttpContext.RequestServices
                            .GetRequiredService<SecurityStampCookieValidator>()
                            .ValidateAsync(context);
                    options.Events.OnRedirectToLogin = context =>
                    {
                        if (context.Request.Headers.ContainsKey("HX-Request"))
                        {
                            context.Response.Headers["HX-Redirect"] = context.RedirectUri;
                            context.Response.StatusCode = 200;
                        }
                        else
                        {
                            context.Response.Redirect(context.RedirectUri);
                        }
                        return Task.CompletedTask;
                    };
                });
        }

        public void ConfigureAuthorizationPolicies()
        {
            builder.Services.AddAuthorizationBuilder()
                .SetDefaultPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(
                        CookieAuthenticationDefaults.AuthenticationScheme, ApiKeyAuthenticationHandler.SchemeName)
                    .RequireAuthenticatedUser()
                    .Build())
                .AddPolicy(nameof(RoleName.User), policy => policy
                    .AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme, ApiKeyAuthenticationHandler.SchemeName)
                    .RequireAuthenticatedUser())
                .AddPolicy(nameof(RoleName.Admin), policy => policy
                    .AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme, ApiKeyAuthenticationHandler.SchemeName)
                    .RequireAuthenticatedUser()
                    .RequireRole(nameof(RoleName.Admin)));
        }

        public void ConfigureLoggingProviders()
        {
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
        }

        public void AddAuthenticationServices()
        {
            builder.Services
                .AddScoped<ISessionStateManager, AspNetSessionStateManager>();

            builder.Services
                .AddScoped<SecurityStampCookieValidator>();

            builder.Services
                .AddScoped<ICurrentUser, CurrentUser>();

            builder.Services
                .AddTransient<IPasswordService, AspNetPasswordService>();
        }
    }
}
