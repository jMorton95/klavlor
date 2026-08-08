global using Microsoft.AspNetCore.Http.HttpResults;
using KlavLor.Web.Authentication;
using KlavLor.Web.Components;
using KlavLor.Web.Configuration;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.AspNetCore.HttpOverrides;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using KlavLor.Application.Common.DependencyInjection;
using KlavLor.Domain;
using KlavLor.Domain.Shared;
using KlavLor.Infrastructure;
using KlavLor.Infrastructure.Persistence.EntityFramework;
using KlavLor.Infrastructure.Services;


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

builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddRazorComponents();

builder.Services.AddDomain();
builder.Services.AddApplication();
// Background services are registered inside AddInfrastructure (they all live in that assembly).
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Per-IP: login attempts
    options.AddPolicy("login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) }));

    // Per-user: standard mutations (node/edge/group CRUD, template CRUD, completion)
    options.AddPolicy("mutation", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));

    // Per-user: high-frequency position updates during drag
    options.AddPolicy("position", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1) }));

    // Per-user: loot ingestion from RuneLite plugin
    options.AddPolicy("loot-ingest", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));

    // Per-IP: anonymous read endpoints
    options.AddPolicy("anonymous", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));

    // Per-IP: SSE feed streams. These are long-lived connections, so the meaningful limit is
    // how many a single IP may hold open at once, not requests per minute — a window limiter
    // would let one client pin 120 sockets for hours. One feed page opens five streams (one
    // per tier), so 20 permits allows ~4 concurrent tabs. QueueLimit 0 rejects immediately
    // with 429 rather than parking the request: a queued EventSource just hangs.
    options.AddPolicy("sse", context =>
        RateLimitPartition.GetConcurrencyLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new ConcurrencyLimiterOptions { PermitLimit = 20, QueueLimit = 0 }));
});

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

// Prime the collection-log cache from the persisted table before any hosted service runs
// (the feed seeder starts immediately and would otherwise classify against an empty set).
var collectionLogCache = scope.ServiceProvider.GetRequiredService<KlavLor.Application.Interfaces.Services.ICollectionLogCache>();
var collectionLogItemRepository = scope.ServiceProvider.GetRequiredService<KlavLor.Domain.Interfaces.Repositories.ICollectionLogItemRepository>();
collectionLogCache.Replace(await collectionLogItemRepository.GetAllEntries());

// Prime the system-settings cache so the sidebar / character pages / feed endpoints
// can branch on feature flags without a DB hit per request.
var systemSettingsCache = scope.ServiceProvider.GetRequiredService<KlavLor.Application.Interfaces.Services.ISystemSettingsCache>();
var systemSettingsRepository = scope.ServiceProvider.GetRequiredService<KlavLor.Domain.Interfaces.Repositories.ISystemSettingsRepository>();
var primedSettings = await systemSettingsRepository.GetOrCreate();
systemSettingsCache.SetLeaguesEnabled(primedSettings.IsLeaguesEnabled);

// Prime the source rate-modifier cache so luck maths reflect admin overrides from the first
// request (the leaderboard refresh and character pages read it on the hot path).
var sourceRateModifierCache = scope.ServiceProvider.GetRequiredService<KlavLor.Application.Interfaces.Services.ISourceRateModifierCache>();
var sourceRateModifierRepository = scope.ServiceProvider.GetRequiredService<KlavLor.Application.Interfaces.Repositories.ISourceRateModifierRepository>();
sourceRateModifierCache.Replace(await sourceRateModifierRepository.GetAll());

// Prime the intrinsic item-value cache before the feed seeder runs: it re-prices drops read back
// out of DropsJson, so an empty cache would classify an overridden item at its raw 0 GP and drop it
// out of the feed entirely.
var itemValueOverrideCache = scope.ServiceProvider.GetRequiredService<KlavLor.Application.Interfaces.Services.IItemValueOverrideCache>();
var itemValueOverrideRepository = scope.ServiceProvider.GetRequiredService<KlavLor.Application.Interfaces.Repositories.IItemValueOverrideRepository>();
itemValueOverrideCache.Replace(await itemValueOverrideRepository.GetAll());

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
app.UseRateLimiter();

app.Use(async (context, next) =>
{
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers.ContentSecurityPolicy =
        "default-src 'self'; script-src 'self' 'unsafe-inline' 'unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https://oldschool.runescape.wiki; font-src 'self'; connect-src 'self'; frame-ancestors 'none'";
    context.Response.Headers["Permissions-Policy"] =
        "camera=(), microphone=(), geolocation=(), payment=()";
    await next();
});


app.MapRazorComponents<App>();

app.MapApplicationRequestHandlers();

app.Run();
