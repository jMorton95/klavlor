global using Microsoft.AspNetCore.Http.HttpResults;
using KlavLor.Web.Authentication;
using KlavLor.Web.Components;
using KlavLor.Web.Configuration;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.AspNetCore.HttpOverrides;
using KlavLor.Application.Common.DependencyInjection;
using KlavLor.Domain;
using KlavLor.Domain.Shared;
using KlavLor.Infrastructure;
using KlavLor.Infrastructure.Persistence.EntityFramework;


var builder = WebApplication.CreateBuilder(args);

builder.Host.UseDefaultServiceProvider((_, options) =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;

    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.WebHost.UseKestrel(options => options.AddServerHeader = false);

builder.Configuration.AddEnvironmentVariables();
builder.Services.AddConfigurationOptions(builder.Configuration);

builder.ConfigureAntiForgeryTokens();
builder.ConfigureAuthenticationCookies();
builder.ConfigureAuthorizationPolicies();
builder.ConfigureResponseCompression();
builder.ConfigureLoggingProviders();

builder.AddAuditInterceptors();
builder.AddAuthenticationServices();

builder.Services.AddHttpContextAccessor();
builder.Services.AddRazorComponents();

builder.Services.AddDomain();
builder.Services.AddApplication();
builder.Services.AddInfrastructure();


if (builder.Environment.IsProduction()) { }
else
{
    StaticWebAssetsLoader.UseStaticWebAssets(builder.Environment, builder.Configuration);
}


var app = builder.Build();

app.UseForwardedHeaders();

using var scope = app.Services.CreateScope();

var migrationService = scope.ServiceProvider.GetRequiredService<IMigrationService>();

await migrationService.ApplyStartupDatabaseMigrations();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHttpsRedirection();
    app.UseHsts();
}

app.UseResponseCompression();

app.MapStaticAssets();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.Use(async (context, next) =>
{
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});


app.MapRazorComponents<App>()
    .RequireAuthorization(nameof(RoleName.User));

app.MapApplicationRequestHandlers();

app.Run();
