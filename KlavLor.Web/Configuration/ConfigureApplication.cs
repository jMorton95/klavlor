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
                .AddCookie(options =>
                {
                    options.Cookie.Name = "KlavLor.Web.Auth";
                    options.Cookie.HttpOnly = true;
                    options.Cookie.IsEssential = true;
                    options.Cookie.SameSite = SameSiteMode.Strict;
                    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                    options.ExpireTimeSpan = TimeSpan.FromHours(3);
                    options.SlidingExpiration = true;
                    options.LoginPath = "/login";
                    options.AccessDeniedPath = "/login";
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
                .AddPolicy(nameof(RoleName.User), policy => policy.RequireAuthenticatedUser())
                .AddPolicy(nameof(RoleName.Admin), policy => policy.RequireAuthenticatedUser().RequireRole(nameof(RoleName.Admin)));
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
                .AddScoped<ICurrentUser, CurrentUser>();

            builder.Services
                .AddTransient<IPasswordService, AspNetPasswordService>();
        }
    }
}
