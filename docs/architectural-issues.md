# Architectural Issues & Remediation Guide

> **Project:** Propel Vulnerability Tracker
> **Date:** 2026-02-09
> **Scope:** Full codebase review across all 5 projects

---

## Table of Contents

- [Critical](#critical)
  - [1. Dual-Host DataContext With Divergent Interceptor Behaviour](#1-dual-host-datacontext-with-divergent-interceptor-behaviour)
  - [2. Worker Swallows Migration Failures](#2-worker-swallows-migration-failures)
  - [3. Secrets Committed in appsettings.json](#3-secrets-committed-in-appsettingsjson)
  - [4. Include Error Detail=true Hardcoded in Connection String](#4-include-error-detailtrue-hardcoded-in-connection-string)
- [High](#high)
  - [5. No Resilience for External API Calls](#5-no-resilience-for-external-api-calls)
  - [6. Worker Pipeline Is All-or-Nothing Sequential](#6-worker-pipeline-is-all-or-nothing-sequential)
  - [7. Zero Test Coverage](#7-zero-test-coverage)
  - [8. No Caching Anywhere](#8-no-caching-anywhere)
  - [9. Reflection-Based DI Registration Is Fragile](#9-reflection-based-di-registration-is-fragile)
- [Medium](#medium)
  - [10. Web Layer Directly References Infrastructure](#10-web-layer-directly-references-infrastructure)
  - [11. Handler Validation Is Boilerplate-Heavy](#11-handler-validation-is-boilerplate-heavy)
  - [12. Domain/Application Repository Split Without Real Separation](#12-domainapplication-repository-split-without-real-separation)
  - [13. Channel-Based Database Logging Can Lose Logs](#13-channel-based-database-logging-can-lose-logs)
  - [14. ForwardedHeaders Trusts All Proxies](#14-forwardedheaders-trusts-all-proxies)
  - [15. Semaphore Misuse in Worker](#15-semaphore-misuse-in-worker)
  - [16. No Rate Limiting or CORS](#16-no-rate-limiting-or-cors)
- [Low](#low)
  - [17. Inconsistent Error Handling Patterns](#17-inconsistent-error-handling-patterns)
  - [18. Magic Numbers Scattered Throughout](#18-magic-numbers-scattered-throughout)
  - [19. Commented-Out RequestAuditMiddleware](#19-commented-out-requestauditmiddleware)
  - [20. Empty Production Branch in Program.cs](#20-empty-production-branch-in-programcs)

---

## Critical

### 1. Dual-Host DataContext With Divergent Interceptor Behaviour

**Location:** `PVT.Web/Program.cs`, `PVT.Worker/Program.cs`, `PVT.Infrastructure/InfrastructureDependencyConfiguration.cs`

**Problem:** Both `PVT.Web` and `PVT.Worker` call `ApplyStartupDatabaseMigrations()` at startup. If both processes start simultaneously (e.g. Docker Compose), you get a migration race condition. More fundamentally, the Web host registers a `UserIdAuditInterceptor` that depends on `IHttpContextAccessor` — but the Worker has no HTTP context. This means the same `DataContext` behaves differently depending on which host resolves it: auditing captures `SavedById` in the Web host but silently produces `null` in the Worker.

**Fix:** Introduce a distributed lock for migrations (only one host runs them) and use an explicit `IAuditContextProvider` abstraction instead of coupling the interceptor to HTTP concerns.

```csharp
// 1. Abstract the audit context away from HTTP
public interface IAuditContextProvider
{
    Task<int?> GetCurrentActorIdAsync();
}

// Web implementation — uses HttpContext
public class HttpAuditContextProvider(IHttpContextAccessor accessor) : IAuditContextProvider
{
    public Task<int?> GetCurrentActorIdAsync()
    {
        var claim = accessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Task.FromResult(int.TryParse(claim, out var id) ? id : (int?)null);
    }
}

// Worker implementation — uses a system-level actor
public class WorkerAuditContextProvider : IAuditContextProvider
{
    public Task<int?> GetCurrentActorIdAsync() => Task.FromResult<int?>(null); // system action
}

// 2. AuditInterceptor now depends on the abstraction
public class AuditInterceptor(IAuditContextProvider contextProvider, TimeProvider timeProvider)
    : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct)
    {
        if (eventData.Context is DataContext context)
        {
            var actorId = await contextProvider.GetCurrentActorIdAsync();
            ApplyAuditData(context, actorId);
        }
        return await base.SavingChangesAsync(eventData, result, ct);
    }
}

// 3. Distributed migration lock (using PostgreSQL advisory lock)
public class SafeMigrationService(DataContext context, ILogger<SafeMigrationService> logger) : IMigrationService
{
    public async Task ApplyStartupDatabaseMigrations()
    {
        const long lockId = 123456789; // Arbitrary unique ID for this app

        await using var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT pg_try_advisory_lock({lockId})";
        var acquired = (bool)(await cmd.ExecuteScalarAsync())!;

        if (!acquired)
        {
            logger.LogInformation("Another instance is running migrations — skipping");
            return;
        }

        try
        {
            await context.Database.MigrateAsync();
        }
        finally
        {
            cmd.CommandText = $"SELECT pg_advisory_unlock({lockId})";
            await cmd.ExecuteScalarAsync();
        }
    }
}
```

---

### 2. Worker Swallows Migration Failures

**Location:** `PVT.Worker/Program.cs:32-44`

**Problem:** The Worker catches the migration exception, logs it, then proceeds to `host.RunAsync()`. The worker starts polling against a potentially broken or outdated schema, leading to runtime failures that are much harder to diagnose than a clean startup crash.

**Fix:** Make migration failure fatal. If the database isn't ready, the worker should not start.

```csharp
// PVT.Worker/Program.cs — BEFORE
try
{
    using var scope = host.Services.CreateScope();
    var migrationService = scope.ServiceProvider.GetRequiredService<IMigrationService>();
    await migrationService.ApplyStartupDatabaseMigrations();
}
catch (Exception ex)
{
    logger.LogError(ex, "Migration Failure");
    // ❌ Continues to host.RunAsync() with broken schema
}

// PVT.Worker/Program.cs — AFTER
try
{
    using var scope = host.Services.CreateScope();
    var migrationService = scope.ServiceProvider.GetRequiredService<IMigrationService>();
    await migrationService.ApplyStartupDatabaseMigrations();
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Migration failure — worker cannot start");
    return; // Exit the process cleanly. Docker/systemd will handle restart policy.
}

try
{
    logger.LogInformation("PVT.Worker started");
    await host.RunAsync();
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Unhandled host error");
}
```

---

### 3. Secrets Committed in appsettings.json

**Location:** `PVT.Web/appsettings.json`, `PVT.Worker/appsettings.json`

**Problem:** Database credentials (`postgres/postgres`), system admin credentials (`josh@pvt.com/password`), GitHub/Bitbucket tokens are all present as placeholder values in source-controlled config files. Even with the `__COMMENT__` noting they're overridden in production, these values work in development and establish a pattern of treating `appsettings.json` as a secrets store.

**Fix:** Remove all secrets from `appsettings.json`. Use .NET User Secrets for local development and environment variables or a vault for production.

```jsonc
// appsettings.json — AFTER (secrets removed, structure preserved)
{
  "DatabaseSettings": {
    "Host": "",
    "Port": "5432",
    "Database": "pvt",
    "Username": "",
    "Password": ""
  },
  "SystemConfiguration": {
    "SystemUsername": "",
    "SystemPassword": ""
  },
  "GitHubConfiguration": {
    "GithubToken": ""
  },
  "BitbucketConfiguration": {
    "BitbucketBaseUrl": "",
    "BitbucketUsername": "",
    "BitbucketToken": ""
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

```bash
# Local development: use dotnet user-secrets
cd PVT.Web
dotnet user-secrets init
dotnet user-secrets set "DatabaseSettings:Host" "localhost"
dotnet user-secrets set "DatabaseSettings:Username" "postgres"
dotnet user-secrets set "DatabaseSettings:Password" "postgres"
dotnet user-secrets set "SystemConfiguration:SystemUsername" "josh@pvt.com"
dotnet user-secrets set "SystemConfiguration:SystemPassword" "localdevpassword"
dotnet user-secrets set "GitHubConfiguration:GithubToken" "ghp_your_token_here"
```

```csharp
// Add startup validation so missing secrets fail fast
public static class ConfigurationValidationExtensions
{
    public static void ValidateRequiredSettings(this IServiceCollection services)
    {
        services.AddOptions<DatabaseSettings>()
            .BindConfiguration(SettingsRegionConstants.DatabaseSettings)
            .Validate(s => !string.IsNullOrWhiteSpace(s.Host), "DatabaseSettings:Host is required")
            .Validate(s => !string.IsNullOrWhiteSpace(s.Username), "DatabaseSettings:Username is required")
            .Validate(s => !string.IsNullOrWhiteSpace(s.Password), "DatabaseSettings:Password is required")
            .ValidateOnStart();

        services.AddOptions<GithubConfiguration>()
            .BindConfiguration(SettingsRegionConstants.GitHubConfiguration)
            .Validate(s => !string.IsNullOrWhiteSpace(s.GitHubToken), "GitHubConfiguration:GithubToken is required")
            .ValidateOnStart();
    }
}
```

---

### 4. Include Error Detail=true Hardcoded in Connection String

**Location:** `PVT.Application/Common/Settings/DatabaseSettings.cs:12`

**Problem:** `ToConnectionString()` unconditionally appends `Include Error Detail=true`, which causes PostgreSQL to return detailed error messages (including column names, constraint names, and internal state) in all environments. In production, this leaks schema internals to any code that surfaces database exceptions.

**Fix:** Make this conditional on the environment, or remove it entirely and rely on server-side logging.

```csharp
// BEFORE
public class DatabaseSettings
{
    public string? Host { get; init; }
    public string? Port { get; init; }
    public string? Database { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }

    public string ToConnectionString() =>
        $"Host={Host};Port={Port};Database={Database};Username={Username};Password={Password};Include Error Detail=true";
}

// AFTER
public class DatabaseSettings
{
    public string? Host { get; init; }
    public string? Port { get; init; }
    public string? Database { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public bool IncludeErrorDetail { get; init; } // Only set to true in Development appsettings

    public string ToConnectionString()
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Port = int.TryParse(Port, out var p) ? p : 5432,
            Database = Database,
            Username = Username,
            Password = Password,
            IncludeErrorDetail = IncludeErrorDetail
        };
        return builder.ConnectionString;
    }
}
```

```jsonc
// appsettings.Development.json
{
  "DatabaseSettings": {
    "IncludeErrorDetail": true
  }
}

// appsettings.json (production default)
{
  "DatabaseSettings": {
    "IncludeErrorDetail": false
  }
}
```

---

## High

### 5. No Resilience for External API Calls

**Location:** `PVT.Infrastructure/ExternalServices/Git/GitHub/GithubApiClient.cs`, `PVT.Infrastructure/ExternalServices/Git/Bitbucket/BitbucketApiClient.cs`, `PVT.Infrastructure/ExternalServices/VulnerabilityDatabases/`

**Problem:** All external API calls (GitHub, Bitbucket, OSV) have zero retry logic, no circuit breakers, and no timeout policies. A single transient network failure means the entire scanning cycle fails with no recovery. GitHub and Bitbucket both have rate limits, and hammering them after a 429 response makes things worse.

**Fix:** Add Polly resilience policies via `Microsoft.Extensions.Http.Resilience`.

```csharp
// Install: dotnet add PVT.Infrastructure package Microsoft.Extensions.Http.Resilience

// In InfrastructureDependencyConfiguration.cs
private void AddBitbucketApiClient()
{
    services.AddHttpClient<IBitbucketApiClient, BitbucketApiClient>((sp, client) =>
    {
        var settings = sp.GetRequiredService<IOptions<BitbucketConfiguration>>().Value;

        if (settings.BitbucketBaseUrl is null || settings.BitbucketUsername is null || settings.BitbucketToken is null)
            throw new InvalidOperationException("Bitbucket configuration is not set.");

        client.BaseAddress = new Uri(settings.BitbucketBaseUrl!);
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.BitbucketUsername}:{settings.BitbucketToken}")));
    })
    .AddStandardResilienceHandler(options =>
    {
        // Retry: 3 attempts with exponential backoff
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.Delay = TimeSpan.FromSeconds(1);
        options.Retry.BackoffType = DelayBackoffType.Exponential;
        options.Retry.ShouldHandle = args => ValueTask.FromResult(
            args.Outcome.Result?.StatusCode is HttpStatusCode.TooManyRequests
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.RequestTimeout
            || args.Outcome.Exception is HttpRequestException or TaskCanceledException);

        // Circuit breaker: open after 5 failures in 30s
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        options.CircuitBreaker.FailureRatioThreshold = 0.5;
        options.CircuitBreaker.MinimumThroughput = 5;
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);

        // Total timeout: 2 minutes per request pipeline
        options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(2);
    });
}

// For the Octokit-based GitHub client, wrap calls with a Polly ResiliencePipeline
public sealed class ResilientGithubApiClient : IGithubApiClient
{
    private readonly GitHubClient _client;
    private readonly ResiliencePipeline _pipeline;
    private readonly ILogger<ResilientGithubApiClient> _logger;

    public ResilientGithubApiClient(
        IOptions<GithubConfiguration> options,
        ILogger<ResilientGithubApiClient> logger)
    {
        _logger = logger;
        var token = options.Value.GitHubToken
            ?? throw new InvalidOperationException("GitHub token is not configured");

        _client = new GitHubClient(new ProductHeaderValue("PVT"))
        {
            Credentials = new Credentials(token)
        };

        _pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder().Handle<ApiException>(ex =>
                    ex.StatusCode is HttpStatusCode.TooManyRequests
                        or HttpStatusCode.ServiceUnavailable
                        or HttpStatusCode.InternalServerError)
            })
            .AddTimeout(TimeSpan.FromSeconds(30))
            .Build();
    }

    public async Task<Result<string>> GetLastRepositoryCommitSha(Repository repository)
    {
        try
        {
            var branch = await _pipeline.ExecuteAsync(async ct =>
                await _client.Repository.Branch.Get(
                    (long)Convert.ToDouble(repository.ExternalRepositoryId),
                    repository.ExternalDefaultBranch));

            return Result<string>.Success(branch.Commit.Sha);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving last commit SHA for repository {RepositoryId}",
                repository.ExternalRepositoryId);
            return Result<string>.Failure("Error connecting to GitHub");
        }
    }
}
```

---

### 6. Worker Pipeline Is All-or-Nothing Sequential

**Location:** `PVT.Worker/WorkerServices/RepositoryScanningWorker.cs:43-58`

**Problem:** `InvokeRepositoryScans()` runs 4 handlers in strict sequence. If `RepositoryChangeTrackingHandler` throws for one repository, the remaining steps (scan, parse, vulnerability lookup) never execute — even for repositories that already have queued work from previous cycles. There is no per-repository error isolation.

**Fix:** Process each repository independently and make each pipeline step resilient to individual failures.

```csharp
public sealed class RepositoryScanningWorker(
    ILogger<RepositoryScanningWorker> logger,
    IServiceScopeFactory serviceScopeFactory
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("{Service} started", nameof(RepositoryScanningWorker));

        using var timer = new PeriodicTimer(Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunPipelineAsync(stoppingToken);
        }
    }

    private async Task RunPipelineAsync(CancellationToken ct)
    {
        logger.LogInformation("Scanning iteration started at {Time}", DateTime.UtcNow);

        using var scope = serviceScopeFactory.CreateScope();
        var services = ResolveServices(scope);

        // Step 1: Change tracking — runs across all repos, logs per-repo failures
        await ExecuteStepSafely("ChangeTracking",
            () => services.RepositoryChangeTrackingHandler.Handle());

        // Step 2: Repository scanning — independent of step 1 succeeding for ALL repos
        await ExecuteStepSafely("RepositoryScan",
            () => services.RepositoryScanHandler.Handle());

        // Step 3: Package file parsing — processes whatever files exist from any prior cycle
        await ExecuteStepSafely("PackageFileParsing",
            () => services.PackageFileParsingHandler.Handle());

        // Step 4: Vulnerability lookup — processes whatever packages exist from any prior cycle
        await ExecuteStepSafely("VulnerabilityLookup",
            () => services.VulnerabilityDatabaseCommandHandler.Handle(LookupCutOffMinutes));

        logger.LogInformation("Scanning iteration finished at {Time}", DateTime.UtcNow);
    }

    private async Task ExecuteStepSafely(string stepName, Func<Task> step)
    {
        try
        {
            await step();
        }
        catch (Exception ex)
        {
            // Log and continue — don't let one step kill the entire pipeline
            logger.LogError(ex, "Pipeline step {StepName} failed", stepName);
        }
    }

    private const int LookupCutOffMinutes = 60;

    private static WorkerRequiredServices ResolveServices(IServiceScope scope)
        => new(
            scope.ServiceProvider.GetRequiredService<RepositoryChangeTrackingHandler>(),
            scope.ServiceProvider.GetRequiredService<RepositoryScanHandler>(),
            scope.ServiceProvider.GetRequiredService<PackageFileParsingHandler>(),
            scope.ServiceProvider.GetRequiredService<VulnerabilityDatabaseLookupHandler>()
        );
}
```

---

### 7. Zero Test Coverage

**Location:** `PVT.Tests/PVT.Tests.csproj`

**Problem:** The test project has xUnit, Moq, and EF Core InMemory configured but contains zero test files. The CI pipeline's test job is commented out. The project namespace is `ClientBooking.Tests` — a copy-paste artifact from another project.

**Fix:** Fix the namespace, add foundational tests for handlers and validators, and re-enable the CI test step.

```xml
<!-- PVT.Tests/PVT.Tests.csproj — fix the namespace -->
<PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>PVT.Tests</RootNamespace> <!-- was ClientBooking.Tests -->
    <IsPackable>false</IsPackable>
</PropertyGroup>
```

```csharp
// PVT.Tests/Features/Clients/ClientCreateHandlerTests.cs
namespace PVT.Tests.Features.Clients;

public class ClientCreateHandlerTests
{
    private readonly Mock<IClientRepository> _clientRepo = new();
    private readonly ClientCreateValidator _validator = new();

    private ClientCreateHandler CreateSut()
        => new(_clientRepo.Object, _validator);

    [Fact]
    public async Task Handle_ValidCommand_CreatesClient()
    {
        // Arrange
        var command = new ClientCreateCommand
        {
            Name = "Acme Corp",
            Description = "A test client",
            IsActive = true
        };

        _clientRepo.Setup(r => r.IsClientNameInUse(command.Name))
            .ReturnsAsync(false);
        _clientRepo.Setup(r => r.SaveClient(It.IsAny<Client>()))
            .ReturnsAsync(true);

        var sut = CreateSut();

        // Act
        var result = await sut.Handle(command);

        // Assert
        Assert.True(result.IsSuccess);
        _clientRepo.Verify(r => r.SaveClient(It.Is<Client>(c => c.Name == "Acme Corp")), Times.Once);
    }

    [Fact]
    public async Task Handle_DuplicateName_ReturnsFailure()
    {
        var command = new ClientCreateCommand
        {
            Name = "Existing Corp",
            Description = "Duplicate",
            IsActive = true
        };

        _clientRepo.Setup(r => r.IsClientNameInUse(command.Name)).ReturnsAsync(true);

        var sut = CreateSut();
        var result = await sut.Handle(command);

        Assert.False(result.IsSuccess);
        Assert.Contains("already exists", result.ErrorMessage);
    }

    [Theory]
    [InlineData("", "Description", "Name is required")]
    [InlineData("A", "Description", "at least 2 characters")]
    [InlineData("Valid", "", "Description is required")]
    public async Task Handle_InvalidCommand_ReturnsValidationErrors(
        string name, string description, string expectedError)
    {
        var command = new ClientCreateCommand
        {
            Name = name,
            Description = description,
            IsActive = true
        };

        var sut = CreateSut();
        var result = await sut.Handle(command);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ValidationErrors);
        Assert.Contains(result.ValidationErrors.Values.SelectMany(v => v),
            msg => msg.Contains(expectedError));
    }
}
```

```yaml
# .github/workflows/pipeline.yml — re-enable test job
jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - name: Restore
        run: dotnet restore
      - name: Build
        run: dotnet build --no-restore
      - name: Test
        run: dotnet test --no-build --verbosity normal

  build-and-push-image:
    needs: test  # Block deployment on green tests
    runs-on: ubuntu-latest
    # ... rest of existing build job
```

---

### 8. No Caching Anywhere

**Location:** All external service clients and query repositories

**Problem:** Every external API call and every database query hits the source fresh every time. GitHub/Bitbucket have rate limits. The OSV vulnerability database gets queried for the same packages repeatedly. Supported package file types are static seed data but queried from the database on every scan cycle.

**Fix:** Add `IMemoryCache` for hot data and HTTP response caching for external APIs.

```csharp
// Register in DI
services.AddMemoryCache();

// Cache supported package files (static seed data that never changes at runtime)
public sealed class CachedSupportedPackageFileRepository(
    ISupportedPackageFileRepository inner,
    IMemoryCache cache) : ISupportedPackageFileRepository
{
    private const string CacheKey = "supported-package-files";

    public async Task<List<SupportedPackageFile>> GetAll()
    {
        return await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            return await inner.GetAll();
        }) ?? [];
    }
}

// Cache vulnerability lookups by package name + version (immutable data)
public sealed class CachedVulnerabilityLookup(
    IVulnerabilityDatabaseStrategy<VulnerabilityDatabaseProvider> inner,
    IMemoryCache cache,
    ILogger<CachedVulnerabilityLookup> logger)
    : IVulnerabilityDatabaseStrategy<VulnerabilityDatabaseProvider>
{
    public VulnerabilityDatabaseProvider Provider => inner.Provider;

    public async Task<List<VulnerabilityResponse>?> QueryVulnerabilities(
        string packageName, string packageVersion, string ecosystem)
    {
        var cacheKey = $"vuln:{ecosystem}:{packageName}:{packageVersion}";

        return await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
            logger.LogDebug("Cache miss for {Package}@{Version}", packageName, packageVersion);
            return await inner.QueryVulnerabilities(packageName, packageVersion, ecosystem);
        });
    }
}

// For Bitbucket HttpClient, add response caching headers support
services.AddHttpClient<IBitbucketApiClient, BitbucketApiClient>(/* ... */)
    .AddStandardResilienceHandler()
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5) // Reuse connections
    });
```

---

### 9. Reflection-Based DI Registration Is Fragile

**Location:** `PVT.Application/Common/DependencyInjection/ApplicationDependencyConfiguration.cs`, `PVT.Infrastructure/InfrastructureDependencyConfiguration.cs:88-124`

**Problem:** Handler registration relies on `t.Name.EndsWith("Handler")` and repository registration relies on `i.Name.EndsWith("Repository")`. A class named `ScanOrchestrator` that should be a handler won't be registered. A repository interface renamed to `IClientDataAccess` silently breaks. No compile-time safety and no startup validation.

**Fix:** Use marker interfaces for explicit opt-in, and add startup validation.

```csharp
// Marker interfaces — explicit opt-in to auto-registration
public interface IHandler { }
public interface IQueryHandler<TQuery, TResult> : IHandler
{
    Task<Result<TResult>> Handle(TQuery query);
}
public interface ICommandHandler<TCommand> : IHandler
{
    Task<Result> Handle(TCommand command);
}

// Handlers implement the interface explicitly
public sealed class ClientCreateHandler(
    IClientRepository clientRepository,
    ClientCreateValidator validator) : ICommandHandler<ClientCreateCommand>
{
    public async Task<Result> Handle(ClientCreateCommand command)
    {
        var validationResult = await validator.ValidateAsync(command);
        if (!validationResult.IsValid)
            return Result.ValidationFailure(validationResult.ToDictionary());

        if (await clientRepository.IsClientNameInUse(command.Name))
            return Result.Failure("A client with this name already exists.");

        var client = Client.Create(command.Name, command.Description, command.IsActive);
        await clientRepository.SaveClient(client);
        return Result.Success();
    }
}

// Registration now scans for the marker interface
public static void AddApplication(this IServiceCollection services)
{
    var assembly = typeof(ApplicationDependencyConfiguration).Assembly;

    var handlerTypes = assembly.DefinedTypes
        .Where(t => t is { IsClass: true, IsAbstract: false }
            && t.ImplementedInterfaces.Any(i => i == typeof(IHandler)
                || (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommandHandler<>))
                || (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))))
        .Select(t => t.AsType());

    foreach (var handlerType in handlerTypes)
    {
        services.TryAddScoped(handlerType);
    }

    services.AddValidatorsFromAssembly(assembly);
}

// Startup validation — fail fast if expected registrations are missing
public class DependencyValidationHostedService(IServiceProvider sp, ILogger<DependencyValidationHostedService> logger)
    : IHostedService
{
    public Task StartAsync(CancellationToken ct)
    {
        using var scope = sp.CreateScope();
        var provider = scope.ServiceProvider;

        // Verify critical services are resolvable
        var criticalTypes = new[]
        {
            typeof(IClientRepository),
            typeof(IUserRepository),
            typeof(IGithubApiClient),
            typeof(IBitbucketApiClient),
        };

        foreach (var type in criticalTypes)
        {
            var service = provider.GetService(type);
            if (service is null)
            {
                logger.LogCritical("Required service {ServiceType} is not registered", type.FullName);
                throw new InvalidOperationException($"Missing required service: {type.FullName}");
            }
        }

        logger.LogInformation("All critical dependencies validated");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

---

## Medium

### 10. Web Layer Directly References Infrastructure

**Location:** `PVT.Web/PVT.Web.csproj` — `<ProjectReference Include="..\PVT.Infrastructure\PVT.Infrastructure.csproj" />`

**Problem:** Clean Architecture dictates that the presentation layer should only depend on the Application layer. The Infrastructure layer should be wired in at the composition root without the Web project having compile-time knowledge of EF Core, Npgsql, or concrete strategy implementations. Currently, `PVT.Web` imports `PVT.Infrastructure` namespaces directly (e.g. `PVT.Infrastructure.Persistence.EntityFramework` in `Program.cs`).

**Fix:** Move infrastructure DI registration to a standalone composition root extension or use a module installer pattern.

```csharp
// Create: PVT.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs
// This is the ONLY public API surface the Web project needs from Infrastructure
namespace PVT.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all infrastructure services. This is the only method
    /// the composition root (Web/Worker) should call.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // All internal wiring happens here — DbContext, repos, strategies, etc.
        // The caller doesn't need to know about DataContext, Npgsql, or any internals.
        return services;
    }
}

// PVT.Web/Program.cs — only calls the extension method
builder.Services.AddDomain();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// The Web project still needs the ProjectReference for the DI method,
// but it should NOT import any internal Infrastructure namespaces.
// Enforce with an .editorconfig rule or architecture test:

// PVT.Tests/ArchitectureTests.cs
[Fact]
public void WebProject_ShouldNotReference_InfrastructureInternals()
{
    var webAssembly = typeof(PVT.Web.Program).Assembly;
    var infraNamespaces = webAssembly.GetReferencedAssemblies()
        .Where(a => a.Name == "PVT.Infrastructure");

    // Scan all types in Web for using directives to Infrastructure internals
    var violations = webAssembly.GetTypes()
        .Where(t => t.Namespace?.Contains("PVT.Web") == true)
        .SelectMany(t => t.GetMembers())
        .Where(m => m.DeclaringType?.Assembly.GetName().Name == "PVT.Infrastructure"
            && !m.DeclaringType.Namespace!.EndsWith("DependencyInjection"))
        .ToList();

    Assert.Empty(violations);
}
```

---

### 11. Handler Validation Is Boilerplate-Heavy

**Location:** Every handler in `PVT.Application/Features/`

**Problem:** Every handler starts with the same 4 lines: call validator, check IsValid, return ValidationFailure. This is repeated identically in `ClientCreateHandler`, `ClientEditHandler`, `UserCreateHandler`, `RepositoryCreateHandler`, etc. Without MediatR pipeline behaviors, there's no way to apply validation as a cross-cutting concern.

**Fix:** Introduce a base handler or a validation decorator.

```csharp
// Option A: Base class that handles validation automatically
public abstract class ValidatedHandler<TCommand, TResult>(AbstractValidator<TCommand>? validator = null)
    where TResult : Result
{
    public async Task<TResult> Handle(TCommand command)
    {
        if (validator is not null)
        {
            var validationResult = await validator.ValidateAsync(command);
            if (!validationResult.IsValid)
                return CreateValidationFailure(validationResult.ToDictionary());
        }

        return await HandleCore(command);
    }

    protected abstract Task<TResult> HandleCore(TCommand command);
    protected abstract TResult CreateValidationFailure(IDictionary<string, string[]> errors);
}

// Concrete handler becomes much cleaner
public sealed class ClientCreateHandler(
    IClientRepository clientRepository,
    ClientCreateValidator validator
) : ValidatedHandler<ClientCreateCommand, Result>(validator)
{
    protected override async Task<Result> HandleCore(ClientCreateCommand command)
    {
        if (await clientRepository.IsClientNameInUse(command.Name))
            return Result.Failure("A client with this name already exists.");

        var client = Client.Create(command.Name, command.Description, command.IsActive);
        await clientRepository.SaveClient(client);
        return Result.Success();
    }

    protected override Result CreateValidationFailure(IDictionary<string, string[]> errors)
        => Result.ValidationFailure(errors);
}

// Option B: Decorator pattern (if you prefer composition over inheritance)
public sealed class ValidationDecorator<TCommand>(
    AbstractValidator<TCommand> validator)
{
    public async Task<Result?> Validate(TCommand command)
    {
        var result = await validator.ValidateAsync(command);
        return result.IsValid ? null : Result.ValidationFailure(result.ToDictionary());
    }
}
```

---

### 12. Domain/Application Repository Split Without Real Separation

**Location:** `PVT.Domain/Interfaces/Repositories/IClientRepository.cs`, `PVT.Application/Interfaces/Repositories/IClientQueryRepository.cs`

**Problem:** The split between `IClientRepository` (writes, in Domain) and `IClientQueryRepository` (reads, in Application) looks like CQRS but doesn't deliver the benefits. Both implementations use the same `DataContext`, same database, same connection. There's no read replica, no separate read model, and no event-driven projection. The split just doubles the number of interfaces and implementations you need to maintain.

**Fix:** Either commit to CQRS properly or consolidate into a single repository interface per aggregate.

```csharp
// Option A: Consolidate (simpler, honest about what's happening)
// Move query methods into the domain repository interface
public interface IClientRepository
{
    // Commands
    Task<bool> SaveClient(Client client);
    Task<bool> DeleteClient(Client client);
    Task<Client?> GetById(int id);
    Task<bool> IsClientNameInUse(string name);

    // Queries (formerly in IClientQueryRepository)
    Task<PagedList<ClientSearchResponse>> Search(PagedQuery query);
    Task<ClientDetailsResponse?> GetDetails(int id);
}

// Option B: Commit to CQRS with a real read model
// Keep the split, but use a separate read-optimized context
public class ClientReadDbContext : DbContext
{
    // Maps to a database VIEW or read-replica
    public DbSet<ClientReadModel> Clients { get; set; }
}

public sealed class ClientQueryRepository(ClientReadDbContext readContext) : IClientQueryRepository
{
    public async Task<PagedList<ClientSearchResponse>> Search(PagedQuery query)
    {
        // Queries go to read-optimized store
        return await readContext.Clients
            .AsNoTracking()
            .SortByProperty(query.SortBy, query.SortDirection)
            .WithPaging(query)
            .ProjectToDto(ClientSpecifications.ToSearchResponse)
            .ToListAsync();
    }
}
```

---

### 13. Channel-Based Database Logging Can Lose Logs

**Location:** `PVT.Infrastructure/Logging/DatabaseLoggerProvider.cs`

**Problem:** `DatabaseLoggerProvider` uses an unbounded `Channel<ErrorLog>` with a background worker that writes to the database. If the application crashes or shuts down abruptly, any logs still in the channel buffer are lost. There's no flush mechanism in `Dispose()` or on graceful shutdown.

**Fix:** Add a bounded channel with backpressure and drain the channel on disposal.

```csharp
public class DatabaseLoggerProvider : ILoggerProvider, IAsyncDisposable
{
    private readonly Channel<ErrorLog> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _backgroundTask;
    private readonly IServiceProvider _services;

    public DatabaseLoggerProvider(IServiceProvider services)
    {
        _services = services;

        // Bounded channel: applies backpressure if logging outpaces persistence
        _channel = Channel.CreateBounded<ErrorLog>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest // Prefer recent logs
        });

        _backgroundTask = Task.Run(BackgroundWorker);
    }

    private async Task BackgroundWorker()
    {
        await foreach (var log in _channel.Reader.ReadAllAsync(_cts.Token))
        {
            try
            {
                using var scope = _services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DataContext>();
                db.ErrorLogs.Add(log);
                await db.SaveChangesAsync();
            }
            catch (Exception)
            {
                // Don't let a DB failure kill the logging pipeline.
                // In production, consider writing to a fallback (file, stderr).
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Signal no more writes, then drain remaining items
        _channel.Writer.TryComplete();

        // Wait for the background worker to finish processing remaining items
        // with a timeout so we don't hang shutdown indefinitely
        using var shutdownCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await _backgroundTask.WaitAsync(shutdownCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Timeout expired — accept the loss of remaining buffered logs
        }

        await _cts.CancelAsync();
        _cts.Dispose();
    }

    void IDisposable.Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public ILogger CreateLogger(string categoryName) => new DatabaseLogger(categoryName, this);
}
```

---

### 14. ForwardedHeaders Trusts All Proxies

**Location:** `PVT.Web/Program.cs:22-29`

**Problem:** Clearing `KnownIPNetworks` and `KnownProxies` tells ASP.NET Core to trust `X-Forwarded-For` and `X-Forwarded-Proto` headers from any source. An attacker can spoof these headers to make the application believe requests come from a different IP or use a different protocol, bypassing IP-based rate limiting or forcing incorrect HTTPS redirects.

**Fix:** Restrict to known proxy networks (your Docker network, load balancer IP, or cloud provider ranges).

```csharp
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;

    // Instead of clearing all restrictions, specify your known proxies
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();

    // Docker internal network (typical range)
    options.KnownNetworks.Add(new IPNetwork(IPAddress.Parse("172.16.0.0"), 12));

    // If behind a specific load balancer, add its IP
    // options.KnownProxies.Add(IPAddress.Parse("10.0.0.1"));

    // Or if you truly need to trust all (e.g. dynamic cloud IPs),
    // at least limit the number of hops
    options.ForwardLimit = 1; // Only trust the immediate proxy
});
```

---

### 15. Semaphore Misuse in Worker

**Location:** `PVT.Worker/WorkerServices/RepositoryScanningWorker.cs:14-41`

**Problem:** A `SemaphoreSlim(1, 1)` is created and used within `ExecuteAsync`, but there's only one caller — the `while` loop in `ExecuteAsync` itself. The semaphore isn't protecting against concurrent access (there's only one thread). It appears to be used as a timer mechanism via `WaitAsync(Interval)`, which is confusing. `PeriodicTimer` is the idiomatic .NET approach for periodic background work.

**Fix:** Replace the semaphore with `PeriodicTimer`.

```csharp
// BEFORE
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        try
        {
            await _semaphoreSlim.WaitAsync(Interval, stoppingToken);
            try
            {
                await InvokeRepositoryScans();
            }
            finally
            {
                _semaphoreSlim.Release();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception in {Service}", nameof(RepositoryScanningWorker));
        }
        await Task.Delay(Interval, stoppingToken);
    }
}

// AFTER
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    logger.LogInformation("{Service} started", nameof(RepositoryScanningWorker));

    using var timer = new PeriodicTimer(Interval);

    while (await timer.WaitForNextTickAsync(stoppingToken))
    {
        try
        {
            await InvokeRepositoryScans();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception in {Service}", nameof(RepositoryScanningWorker));
        }
    }
}
```

---

### 16. No Rate Limiting or CORS

**Location:** `PVT.Web/Program.cs` (absent)

**Problem:** There is no middleware-level rate limiting. The login endpoint has application-level lockout logic (`AccessFailedCount`, `IsLockedOut`) but nothing stopping an attacker from hammering the endpoint at high volume. There's also no CORS policy — if any API endpoint needs to be consumed by a different origin in the future, it'll fail silently.

**Fix:** Add ASP.NET Core rate limiting middleware.

```csharp
// In ConfigureApplication.cs or Program.cs
builder.Services.AddRateLimiter(options =>
{
    // Global rate limit
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    // Stricter limit for login endpoint
    options.AddPolicy("login", context =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 2,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// In the middleware pipeline (after UseRouting, before UseAuthentication)
app.UseRateLimiter();

// Apply stricter policy to login endpoint
app.MapPost(AppRoutes.Login, LoginEndpoint.Handle)
    .RequireRateLimiting("login");
```

---

## Low

### 17. Inconsistent Error Handling Patterns

**Location:** Various handlers and repository implementations across the solution

**Problem:** Some methods throw `RepositoryException`, some return `Result.Failure()`, some catch broad `Exception` and some catch specific types. The `RepositoryException` is the only custom exception, but it's used inconsistently — some repositories throw it, others return boolean success/failure, and handlers sometimes catch it to convert to a `Result` and sometimes let it propagate.

**Fix:** Standardise: repositories throw exceptions (they're infrastructure), handlers catch and convert to `Result`.

```csharp
// Repository layer: always throws on failure (infrastructure concern)
internal sealed class ClientRepository(DataContext dataContext, ILogger<ClientRepository> logger)
    : IClientRepository
{
    public async Task<Client?> GetById(int id)
    {
        try
        {
            return await dataContext.Clients.FirstOrDefaultAsync(x => x.Id == id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get Client by id {ClientId}", id);
            throw new RepositoryException($"Failed to get Client by id {id}", ex);
        }
    }

    // Returns the entity, not a bool — let the caller decide what "success" means
    public async Task<Client> SaveClient(Client client)
    {
        try
        {
            if (client.Id == 0)
                await dataContext.Clients.AddAsync(client);
            else
                dataContext.Clients.Update(client);

            await dataContext.SaveChangesAsync();
            return client;
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Failed to save Client {ClientId}", client.Id);
            throw new RepositoryException($"Failed to save Client {client.Id}", ex);
        }
    }
}

// Handler layer: catches repository exceptions and converts to Result
public sealed class ClientCreateHandler(
    IClientRepository clientRepository,
    ClientCreateValidator validator) : ICommandHandler<ClientCreateCommand>
{
    public async Task<Result> Handle(ClientCreateCommand command)
    {
        var validationResult = await validator.ValidateAsync(command);
        if (!validationResult.IsValid)
            return Result.ValidationFailure(validationResult.ToDictionary());

        try
        {
            if (await clientRepository.IsClientNameInUse(command.Name))
                return Result.Failure("A client with this name already exists.");

            var client = Client.Create(command.Name, command.Description, command.IsActive);
            await clientRepository.SaveClient(client);
            return Result.Success();
        }
        catch (RepositoryException ex)
        {
            return Result.Failure(ex.Message);
        }
    }
}
```

---

### 18. Magic Numbers Scattered Throughout

**Location:** Multiple files

**Problem:** Hardcoded values like `LookupCutOffMinutes = 60`, `TimeSpan.FromSeconds(15)`, `TimeSpan.FromHours(3)`, `maxDepth = 10000` are scattered across the codebase with no central configuration. Changing the worker polling interval requires a code change and redeployment.

**Fix:** Centralise into a configuration class bound to `appsettings.json`.

```csharp
// New: PVT.Application/Common/Settings/WorkerSettings.cs
public sealed class WorkerSettings
{
    public int ScanIntervalSeconds { get; init; } = 15;
    public int VulnerabilityLookupCutoffMinutes { get; init; } = 60;
    public int BitbucketMaxDirectoryDepth { get; init; } = 10_000;
}

// New: PVT.Application/Common/Settings/AuthSettings.cs
public sealed class AuthSettings
{
    public int SessionExpirationHours { get; init; } = 3;
    public string CookieName { get; init; } = "PVT.Web.Auth";
    public string AntiForgeryCookieName { get; init; } = "PVT.Web";
}
```

```jsonc
// appsettings.json
{
  "WorkerSettings": {
    "ScanIntervalSeconds": 15,
    "VulnerabilityLookupCutoffMinutes": 60,
    "BitbucketMaxDirectoryDepth": 10000
  },
  "AuthSettings": {
    "SessionExpirationHours": 3,
    "CookieName": "PVT.Web.Auth",
    "AntiForgeryCookieName": "PVT.Web"
  }
}
```

```csharp
// Usage in worker
public sealed class RepositoryScanningWorker(
    IOptions<WorkerSettings> settings,
    ILogger<RepositoryScanningWorker> logger,
    IServiceScopeFactory serviceScopeFactory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(settings.Value.ScanIntervalSeconds);
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            // ...
            await services.VulnerabilityDatabaseCommandHandler
                .Handle(settings.Value.VulnerabilityLookupCutoffMinutes);
        }
    }
}

// Usage in auth config
public void ConfigureAuthenticationCookies()
{
    var authSettings = configuration.GetSection("AuthSettings").Get<AuthSettings>()!;

    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.Cookie.Name = authSettings.CookieName;
            options.ExpireTimeSpan = TimeSpan.FromHours(authSettings.SessionExpirationHours);
            // ...
        });
}
```

---

### 19. Commented-Out RequestAuditMiddleware

**Location:** `PVT.Web/Program.cs:98`

**Problem:** `//app.UseMiddleware<RequestAuditMiddleware>();` is dead code that signals an incomplete feature. It's unclear whether this was disabled temporarily for debugging or abandoned. Either way, commented-out code in the middleware pipeline is confusing — future developers won't know whether to enable it.

**Fix:** Either implement it properly and enable it, or remove it entirely with a tracking issue.

```csharp
// Option A: Remove the comment and create a GitHub issue to track the work
// Delete line 98 entirely and create:
// GitHub Issue: "Implement request audit middleware for compliance logging"

// Option B: Implement it properly behind a feature flag
public class RequestAuditMiddleware(RequestDelegate next, ILogger<RequestAuditMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();
            logger.LogInformation(
                "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms for user {UserId}",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                userId ?? "anonymous");
        }
    }
}

// Register conditionally
if (app.Configuration.GetValue<bool>("Features:RequestAuditing"))
{
    app.UseMiddleware<RequestAuditMiddleware>();
}
```

---

### 20. Empty Production Branch in Program.cs

**Location:** `PVT.Web/Program.cs:55-59`

**Problem:** The code `if (builder.Environment.IsProduction()) { }` is a no-op block that does nothing. It's likely a placeholder for production-specific configuration that was never implemented.

**Fix:** Either add the intended production configuration or remove the empty block.

```csharp
// BEFORE
if (builder.Environment.IsProduction()) { }
else
{
    StaticWebAssetsLoader.UseStaticWebAssets(builder.Environment, builder.Configuration);
}

// AFTER — just use the negated condition directly
if (!builder.Environment.IsProduction())
{
    StaticWebAssetsLoader.UseStaticWebAssets(builder.Environment, builder.Configuration);
}
```
