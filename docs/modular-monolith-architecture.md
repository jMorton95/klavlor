# Modular Monolith Architecture & Migration Guide

> **Project:** Propel Vulnerability Tracker
> **Date:** 2026-02-09
> **Purpose:** Architecture analysis, modular monolith design, and step-by-step migration plan

---

## Table of Contents

- [Current Architecture Analysis](#current-architecture-analysis)
  - [Dependency Graph](#dependency-graph)
  - [Data Flow](#data-flow)
  - [What Works Well](#what-works-well)
  - [Structural Problems](#structural-problems)
- [Target Architecture: Modular Monolith](#target-architecture-modular-monolith)
  - [Design Principles](#design-principles)
  - [Module Map](#module-map)
  - [Module Communication](#module-communication)
  - [Directory Structure](#directory-structure)
  - [Module Internals](#module-internals)
  - [Shared Kernel](#shared-kernel)
  - [Host Projects](#host-projects)
  - [Database Strategy](#database-strategy)
- [Migration Guide](#migration-guide)
  - [Phase 0: Foundation](#phase-0-foundation)
  - [Phase 1: Extract SharedKernel](#phase-1-extract-sharedkernel)
  - [Phase 2: Extract Identity Module](#phase-2-extract-identity-module)
  - [Phase 3: Extract Clients Module](#phase-3-extract-clients-module)
  - [Phase 4: Extract Auditing Module](#phase-4-extract-auditing-module)
  - [Phase 5: Extract Repositories Module](#phase-5-extract-repositories-module)
  - [Phase 6: Extract Scanning Module](#phase-6-extract-scanning-module)
  - [Phase 7: Extract Vulnerabilities Module](#phase-7-extract-vulnerabilities-module)
  - [Phase 8: Cleanup & Validation](#phase-8-cleanup--validation)
- [Key Implementation Patterns](#key-implementation-patterns)
  - [Module Installer Contract](#module-installer-contract)
  - [Module Contracts (Public API)](#module-contracts-public-api)
  - [Integration Events](#integration-events)
  - [Per-Module DbContext](#per-module-dbcontext)
  - [Architecture Tests](#architecture-tests)

---

## Current Architecture Analysis

### Dependency Graph

```
┌─────────────────────────────────────────────────────────────┐
│                        HOSTS                                 │
│                                                              │
│   ┌──────────────┐              ┌──────────────────┐        │
│   │   PVT.Web    │              │   PVT.Worker     │        │
│   │  (ASP.NET)   │              │ (BackgroundSvc)  │        │
│   └──────┬───────┘              └────────┬─────────┘        │
│          │                                │                  │
└──────────┼────────────────────────────────┼──────────────────┘
           │                                │
           │  ┌─────────────────────────┐   │
           ├──│   PVT.Infrastructure    │───┤
           │  │  EF Core, APIs, Repos   │   │
           │  └───────────┬─────────────┘   │
           │              │                 │
           │  ┌───────────┴─────────────┐   │
           ├──│    PVT.Application      │───┤
           │  │ Handlers, Validators,   │   │
           │  │ DTOs, Interfaces        │   │
           │  └───────────┬─────────────┘   │
           │              │                 │
           │  ┌───────────┴─────────────┐   │
           └──│      PVT.Domain         │───┘
              │  Entities, Enums,       │
              │  Factory, Base Types    │
              └─────────────────────────┘
```

**Problems visible in this graph:**

1. **PVT.Web references PVT.Infrastructure directly** — bypasses the Application layer boundary
2. **Both hosts depend on all 3 inner layers** — no isolation between Web-specific and Worker-specific concerns
3. **Single DataContext shared across both hosts** — 13+ DbSets, one connection pool config, divergent interceptor behavior
4. **No module boundaries** — any handler can inject any repository from any domain area

### Data Flow

```
                    ┌─────────────────────────────┐
                    │   External Source Control    │
                    │   (GitHub / Bitbucket)       │
                    └──────────────┬──────────────┘
                                   │
                    ┌──────────────▼──────────────┐
                    │  1. ChangeTrackingHandler    │
                    │  Check for new commits       │
                    │  Update LastTrackedCommit     │
                    └──────────────┬──────────────┘
                                   │
                    ┌──────────────▼──────────────┐
                    │  2. RepositoryScanHandler    │
                    │  Fetch package files from     │
                    │  remote (via scan strategies) │
                    │  Create RepositoryScan +      │
                    │  RepositoryPackageFiles        │
                    └──────────────┬──────────────┘
                                   │
                    ┌──────────────▼──────────────┐
                    │  3. PackageFileParsingHandler │
                    │  Parse .csproj / package.json │
                    │  Extract RepositoryPackages    │
                    └──────────────┬──────────────┘
                                   │
                    ┌──────────────▼──────────────┐
                    │  4. VulnerabilityLookup      │
                    │     Handler                   │
                    │  Query OSV database            │
                    │  Create Vulnerability records  │
                    └──────────────┬──────────────┘
                                   │
                    ┌──────────────▼──────────────┐
                    │         PostgreSQL            │
                    │  (Single shared database)     │
                    └──────────────┬──────────────┘
                                   │
                    ┌──────────────▼──────────────┐
                    │       PVT.Web UI             │
                    │  Razor Components + HTMX     │
                    │  Displays results to users    │
                    └─────────────────────────────┘
```

**Problem:** Steps 1-4 run sequentially in a single pipeline. If step 1 fails, steps 2-4 are skipped even when they have work queued from prior cycles.

### What Works Well

| Aspect | Implementation | Assessment |
|--------|---------------|------------|
| Dependency direction | Domain has zero references; outer layers point inward | Correct |
| Strategy pattern | `IRepositoryScanStrategy<T>`, `IPackageFileParsingStrategy<T>` | Extensible — adding GitLab or Maven is straightforward |
| Result pattern | `Result<T>` with `IsSuccess`, `ErrorMessage`, `ValidationErrors` | Explicit error handling, no hidden exceptions |
| FluentValidation | Validators auto-registered from assembly, used in all handlers | Consistent input validation |
| Audit interceptor | `SaveChangesInterceptor` captures `SavedAt`, `SavedById`, `RowVersion` | Transparent auditing |
| Endpoint pattern | `IEndpoint` with `MapEndpoint()` for minimal APIs | Clean, testable endpoint registration |
| Specification pattern | Expression-tree projections via `ProjectToDto()` | Efficient EF Core queries without materialising full entities |
| Entity base class | `Entity` with `Id`, `RowVersion`, `SavedAt`, `SavedBy` | Consistent across all domain types |

### Structural Problems

```
   CURRENT STRUCTURE                    PROBLEM
   ─────────────────                    ───────

   PVT.Application/
   ├── Features/
   │   ├── Clients/                     ─── All features share one DI container
   │   ├── Users/                           with no access restrictions. The
   │   ├── Repositories/                    UserCreateHandler can inject
   │   ├── Scanning/                        IClientRepository. There are no
   │   └── Vulnerabilities/                 module boundaries.
   └── Interfaces/
       └── Repositories/               ─── 12+ repository interfaces all in
           ├── IClientQueryRepository       one namespace. No ownership model.
           ├── IUserQueryRepository         Who "owns" IPackageRepository?
           ├── IPackageQueryRepository      Scanning? Vulnerabilities? Both?
           └── ...

   PVT.Infrastructure/
   └── Persistence/
       └── EntityFramework/
           ├── DataContext.cs           ─── 13 DbSets. One migration history.
           │                                One connection pool. Changing the
           │                                Vulnerability schema requires
           │                                touching the same context as Client.
           └── Repositories/
               ├── ClientRepository     ─── All repositories can see all tables.
               ├── UserRepository           No enforcement of aggregate boundaries.
               └── ...                      ClientRepository could query Users.

   PVT.Web/
   └── Program.cs                       ─── References PVT.Infrastructure
                                            directly for migration service,
                                            logging provider, and DbContext.
                                            Clean Architecture boundary broken.
```

---

## Target Architecture: Modular Monolith

### Design Principles

1. **Module autonomy** — Each module owns its domain, data access, and business logic. Modules cannot reach into each other's internals.
2. **Explicit contracts** — Modules expose a `Contracts/` folder with interfaces and DTOs. This is the only public API surface.
3. **Own your data** — Each module has its own `DbContext` mapping only its own tables. Same physical database, but logical isolation.
4. **Communicate through contracts or events** — Direct repository cross-references are replaced with module interface calls or integration events.
5. **Thin hosts** — `PVT.Host.Web` and `PVT.Host.Worker` are composition roots only. Zero business logic. They install modules and wire middleware.

### Module Map

```
┌─────────────────────────────────────────────────────────────────────┐
│                          HOST LAYER                                  │
│                                                                      │
│   ┌───────────────────┐              ┌────────────────────┐         │
│   │   PVT.Host.Web    │              │  PVT.Host.Worker   │         │
│   │   Composition     │              │   Composition      │         │
│   │   Root Only       │              │   Root Only        │         │
│   └────────┬──────────┘              └─────────┬──────────┘         │
│            │  installs modules                  │  installs modules  │
└────────────┼────────────────────────────────────┼────────────────────┘
             │                                    │
┌────────────┼────────────────────────────────────┼────────────────────┐
│            ▼           MODULE LAYER              ▼                    │
│                                                                      │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐                │
│  │   Identity   │ │   Clients    │ │ Repositories │                │
│  │              │ │              │ │              │                │
│  │ Users, Roles │ │ Client CRUD  │ │ Repo CRUD   │                │
│  │ Auth, Login  │ │ Client Mgmt  │ │ Repo Mgmt   │                │
│  └──────┬───────┘ └──────┬───────┘ └──────┬───────┘                │
│         │                │                │                          │
│  ┌──────┴───────┐ ┌──────┴───────┐ ┌──────┴───────┐                │
│  │   Scanning   │ │Vulnerabilit. │ │   Auditing   │                │
│  │              │ │              │ │              │                │
│  │ Git Clients  │ │ OSV Lookup   │ │ Audit Logs   │                │
│  │ File Parsing │ │ CVSS Scoring │ │ Error Logs   │                │
│  │ Scan Pipeline│ │ Vuln Storage │ │ Notifications│                │
│  └──────────────┘ └──────────────┘ └──────────────┘                │
│                                                                      │
└──────────────────────────────────────────────────────────────────────┘
             │
┌────────────┼─────────────────────────────────────────────────────────┐
│            ▼         SHARED KERNEL                                    │
│                                                                      │
│  ┌────────────────────────────────────────────────────────────────┐  │
│  │                     PVT.SharedKernel                            │  │
│  │                                                                │  │
│  │  Entity, Result<T>, PagedQuery, PagedList, IModuleInstaller,  │  │
│  │  IDomainEvent, Integration Event Contracts                     │  │
│  └────────────────────────────────────────────────────────────────┘  │
│                                                                      │
└──────────────────────────────────────────────────────────────────────┘
```

### Module Communication

Modules never reference each other's internal projects. They communicate via **contracts** (synchronous) or **integration events** (asynchronous/decoupled).

```
  ┌──────────────┐         Contract Call          ┌──────────────────┐
  │   Scanning   │ ──────────────────────────────► │   Repositories   │
  │   Module     │   "Give me repos due for scan"  │   Module         │
  │              │ ◄────────────────────────────── │                  │
  │              │   List<RepositorySummaryDto>     │                  │
  └──────┬───────┘                                 └──────────────────┘
         │
         │  Integration Event
         │  "PackagesDiscoveredEvent"
         │
         ▼
  ┌──────────────────┐
  │  Vulnerabilities  │   Subscribes to event,
  │  Module           │   runs OSV lookup for
  │                   │   new packages
  └──────────────────┘

  ┌──────────────┐         Contract Call          ┌──────────────────┐
  │   Clients    │ ──────────────────────────────► │   Identity       │
  │   Module     │   "Is user ID 5 an admin?"      │   Module         │
  │              │ ◄────────────────────────────── │                  │
  │              │   bool                          │                  │
  └──────────────┘                                 └──────────────────┘
```

**Rules:**
- **Contracts** = synchronous method calls via interfaces. Used when the caller needs a response immediately.
- **Integration Events** = fire-and-forget notifications. Used when the publisher doesn't care about the result.
- **No shared DbContext** — if module A needs data from module B, it calls B's contract, never B's database tables.

### Directory Structure

```
src/
│
├── PVT.SharedKernel/                          # Shared building blocks
│   ├── PVT.SharedKernel.csproj
│   ├── Domain/
│   │   ├── Entity.cs                          # Base entity (Id, RowVersion, SavedAt)
│   │   └── IDomainEvent.cs                    # Marker interface
│   ├── Application/
│   │   ├── Result.cs                          # Result<T> pattern
│   │   ├── PagedQuery.cs
│   │   ├── PagedList.cs
│   │   └── Pagination.cs
│   ├── Infrastructure/
│   │   └── IModuleInstaller.cs                # Module registration contract
│   └── IntegrationEvents/
│       ├── PackagesDiscoveredEvent.cs
│       ├── VulnerabilitiesFoundEvent.cs
│       └── RepositoryScannedEvent.cs
│
├── Modules/
│   │
│   ├── PVT.Modules.Identity/                  # Identity & Authentication
│   │   ├── PVT.Modules.Identity.csproj
│   │   ├── Contracts/                         # PUBLIC — other modules reference this
│   │   │   ├── IIdentityModule.cs
│   │   │   ├── ICurrentUserService.cs
│   │   │   └── Dtos/
│   │   │       └── UserSummaryDto.cs
│   │   ├── Domain/
│   │   │   ├── User.cs
│   │   │   ├── Role.cs
│   │   │   ├── UserRole.cs
│   │   │   └── UserLoginService.cs
│   │   ├── Application/
│   │   │   ├── Commands/
│   │   │   │   ├── CreateUser/
│   │   │   │   │   ├── CreateUserCommand.cs
│   │   │   │   │   ├── CreateUserHandler.cs
│   │   │   │   │   └── CreateUserValidator.cs
│   │   │   │   ├── EditUser/
│   │   │   │   ├── DeleteUser/
│   │   │   │   └── ToggleAdmin/
│   │   │   └── Queries/
│   │   │       └── SearchUsers/
│   │   │           ├── SearchUsersQuery.cs
│   │   │           ├── SearchUsersHandler.cs
│   │   │           └── UserSearchResponse.cs
│   │   ├── Infrastructure/
│   │   │   ├── IdentityDbContext.cs
│   │   │   ├── Configuration/
│   │   │   │   ├── UserEntityConfiguration.cs
│   │   │   │   └── RoleEntityConfiguration.cs
│   │   │   ├── Repositories/
│   │   │   │   ├── UserRepository.cs
│   │   │   │   └── RoleRepository.cs
│   │   │   └── Services/
│   │   │       └── AspNetPasswordService.cs
│   │   ├── Endpoints/
│   │   │   ├── LoginEndpoint.cs
│   │   │   ├── LogoutEndpoint.cs
│   │   │   ├── UserCreateEndpoint.cs
│   │   │   ├── UserEditEndpoint.cs
│   │   │   └── UserSearchEndpoint.cs
│   │   └── IdentityModuleInstaller.cs
│   │
│   ├── PVT.Modules.Clients/                   # Client Management
│   │   ├── PVT.Modules.Clients.csproj
│   │   ├── Contracts/
│   │   │   ├── IClientModule.cs
│   │   │   └── Dtos/
│   │   │       └── ClientSummaryDto.cs
│   │   ├── Domain/
│   │   │   └── Client.cs
│   │   ├── Application/
│   │   │   ├── Commands/
│   │   │   │   ├── CreateClient/
│   │   │   │   ├── EditClient/
│   │   │   │   └── DeleteClient/
│   │   │   └── Queries/
│   │   │       ├── SearchClients/
│   │   │       └── ClientDetails/
│   │   ├── Infrastructure/
│   │   │   ├── ClientsDbContext.cs
│   │   │   └── Repositories/
│   │   ├── Endpoints/
│   │   └── ClientsModuleInstaller.cs
│   │
│   ├── PVT.Modules.Repositories/              # Repository Management
│   │   ├── PVT.Modules.Repositories.csproj
│   │   ├── Contracts/
│   │   │   ├── IRepositoryModule.cs
│   │   │   └── Dtos/
│   │   │       ├── RepositorySummaryDto.cs
│   │   │       └── RepositoryScanDueDto.cs
│   │   ├── Domain/
│   │   │   ├── Repository.cs
│   │   │   └── RepositoryScan.cs
│   │   ├── Application/
│   │   │   ├── Commands/
│   │   │   │   ├── CreateRepository/
│   │   │   │   ├── EditRepository/
│   │   │   │   ├── DeleteRepository/
│   │   │   │   └── RequestManualScan/
│   │   │   └── Queries/
│   │   │       ├── SearchRepositories/
│   │   │       └── RepositoryDetails/
│   │   ├── Infrastructure/
│   │   │   ├── RepositoriesDbContext.cs
│   │   │   └── Repositories/
│   │   ├── Endpoints/
│   │   └── RepositoriesModuleInstaller.cs
│   │
│   ├── PVT.Modules.Scanning/                  # Core Scanning Pipeline
│   │   ├── PVT.Modules.Scanning.csproj
│   │   ├── Contracts/
│   │   │   └── IScanningModule.cs
│   │   ├── Domain/
│   │   │   ├── RepositoryPackageFile.cs
│   │   │   ├── RepositoryPackage.cs
│   │   │   └── SupportedPackageFile.cs
│   │   ├── Application/
│   │   │   ├── Pipeline/
│   │   │   │   ├── IScanPipelineStep.cs
│   │   │   │   ├── ScanPipelineOrchestrator.cs
│   │   │   │   ├── ChangeTrackingStep.cs
│   │   │   │   ├── FileScanStep.cs
│   │   │   │   └── PackageParseStep.cs
│   │   │   └── Strategies/
│   │   │       ├── IScanStrategy.cs
│   │   │       ├── IChangeTrackingStrategy.cs
│   │   │       └── IParsingStrategy.cs
│   │   ├── Infrastructure/
│   │   │   ├── ScanningDbContext.cs
│   │   │   ├── Providers/
│   │   │   │   ├── GitHub/
│   │   │   │   │   ├── GithubApiClient.cs
│   │   │   │   │   ├── GithubScanStrategy.cs
│   │   │   │   │   ├── GithubChangeTrackingStrategy.cs
│   │   │   │   │   └── GithubLookupStrategy.cs
│   │   │   │   └── Bitbucket/
│   │   │   │       ├── BitbucketApiClient.cs
│   │   │   │       ├── BitbucketScanStrategy.cs
│   │   │   │       ├── BitbucketChangeTrackingStrategy.cs
│   │   │   │       └── BitbucketLookupStrategy.cs
│   │   │   ├── Parsers/
│   │   │   │   ├── CsProjParser.cs
│   │   │   │   └── PackageJsonParser.cs
│   │   │   └── Repositories/
│   │   ├── Endpoints/
│   │   │   └── ManualScanEndpoint.cs
│   │   └── ScanningModuleInstaller.cs
│   │
│   ├── PVT.Modules.Vulnerabilities/           # Vulnerability Data
│   │   ├── PVT.Modules.Vulnerabilities.csproj
│   │   ├── Contracts/
│   │   │   ├── IVulnerabilityModule.cs
│   │   │   └── Dtos/
│   │   │       └── VulnerabilitySummaryDto.cs
│   │   ├── Domain/
│   │   │   ├── Vulnerability.cs
│   │   │   └── VulnerabilitySource.cs
│   │   ├── Application/
│   │   │   ├── Commands/
│   │   │   │   └── LookupVulnerabilities/
│   │   │   └── Queries/
│   │   │       └── PackageVulnerabilityDetails/
│   │   ├── Infrastructure/
│   │   │   ├── VulnerabilitiesDbContext.cs
│   │   │   ├── ExternalApis/
│   │   │   │   └── OsvDatabaseStrategy.cs
│   │   │   ├── Services/
│   │   │   │   └── CvssCalculatorService.cs
│   │   │   └── Repositories/
│   │   ├── Endpoints/
│   │   └── VulnerabilitiesModuleInstaller.cs
│   │
│   └── PVT.Modules.Auditing/                  # Cross-Cutting Auditing
│       ├── PVT.Modules.Auditing.csproj
│       ├── Contracts/
│       │   └── IAuditingModule.cs
│       ├── Domain/
│       │   ├── AuditLog.cs
│       │   ├── ErrorLog.cs
│       │   └── Notification.cs
│       ├── Infrastructure/
│       │   ├── AuditingDbContext.cs
│       │   ├── Interceptors/
│       │   │   └── AuditInterceptor.cs
│       │   ├── Logging/
│       │   │   ├── DatabaseLoggerProvider.cs
│       │   │   └── DatabaseLogger.cs
│       │   └── Repositories/
│       ├── Endpoints/
│       │   ├── AuditLogEndpoint.cs
│       │   ├── ErrorLogEndpoint.cs
│       │   └── NotificationEndpoint.cs
│       └── AuditingModuleInstaller.cs
│
├── PVT.Host.Web/                              # Thin ASP.NET host
│   ├── PVT.Host.Web.csproj
│   ├── Program.cs
│   ├── Components/                            # Razor layout + shared UI
│   ├── wwwroot/
│   └── appsettings.json
│
├── PVT.Host.Worker/                           # Thin Worker host
│   ├── PVT.Host.Worker.csproj
│   ├── Program.cs
│   ├── ScanningWorkerService.cs
│   └── appsettings.json
│
└── tests/
    ├── PVT.Modules.Identity.Tests/
    ├── PVT.Modules.Clients.Tests/
    ├── PVT.Modules.Repositories.Tests/
    ├── PVT.Modules.Scanning.Tests/
    ├── PVT.Modules.Vulnerabilities.Tests/
    ├── PVT.Modules.Auditing.Tests/
    └── PVT.IntegrationTests/
```

### Module Internals

Each module follows the same internal layering, but contained within a single project:

```
PVT.Modules.Scanning/
│
├── Contracts/                    ◄── PUBLIC: Other modules can reference
│   ├── IScanningModule.cs            these types. Everything else in
│   └── Dtos/                         the module is internal.
│       └── ScanResultDto.cs
│
├── Domain/                       ◄── INTERNAL: Entities, value objects,
│   ├── RepositoryPackageFile.cs       domain logic. Not visible outside
│   ├── RepositoryPackage.cs           this module.
│   └── SupportedPackageFile.cs
│
├── Application/                  ◄── INTERNAL: Handlers, validators,
│   ├── Pipeline/                      orchestration. Uses Domain +
│   │   ├── IScanPipelineStep.cs       Infrastructure internally.
│   │   └── ScanPipelineOrchestrator.cs
│   └── Strategies/
│       ├── IScanStrategy.cs
│       └── IParsingStrategy.cs
│
├── Infrastructure/               ◄── INTERNAL: DbContext, repos, API
│   ├── ScanningDbContext.cs           clients. Implements Application
│   ├── Providers/                     interfaces.
│   │   ├── GitHub/
│   │   └── Bitbucket/
│   └── Parsers/
│
├── Endpoints/                    ◄── INTERNAL: Minimal API endpoints.
│   └── ManualScanEndpoint.cs          Registered via the module installer.
│
└── ScanningModuleInstaller.cs    ◄── PUBLIC: The single entry point
                                       for the host to install this module.
```

**Access control:** Use `internal` for all classes except those in `Contracts/` and the `ModuleInstaller`. The module's `.csproj` does NOT use `InternalsVisibleTo` for other modules — only for its own test project.

### Shared Kernel

The SharedKernel contains only types that genuinely belong to no single module:

```csharp
// PVT.SharedKernel/Domain/Entity.cs
public abstract class Entity
{
    [Key, Required] public int Id { get; set; }
    [Required] public uint RowVersion { get; set; }
    [Required] public DateTimeOffset SavedAt { get; set; }
    public int? SavedById { get; set; }
}

// PVT.SharedKernel/Application/Result.cs
public abstract class Result(bool isSuccess, string error, IDictionary<string, string[]>? validationErrors = null)
{
    public bool IsSuccess { get; } = isSuccess;
    public string ErrorMessage { get; } = error;
    public IDictionary<string, string[]>? ValidationErrors { get; } = validationErrors;

    public static Result Success() => new Result<NoValue>(NoValue.Instance, true, string.Empty);
    public static Result Failure(string error) => new Result<NoValue>(NoValue.Instance, false, error);
    public static Result ValidationFailure(IDictionary<string, string[]> errors) =>
        new Result<NoValue>(NoValue.Instance, false, "Validation failed", errors);
}

// PVT.SharedKernel/Infrastructure/IModuleInstaller.cs
public interface IModuleInstaller
{
    void Install(IServiceCollection services, IConfiguration configuration);
    void MapEndpoints(IEndpointRouteBuilder app);
}

// PVT.SharedKernel/IntegrationEvents/PackagesDiscoveredEvent.cs
public sealed record PackagesDiscoveredEvent(
    int RepositoryId,
    List<DiscoveredPackage> Packages,
    DateTimeOffset DiscoveredAt);

public sealed record DiscoveredPackage(
    string PackageName,
    string PackageVersion,
    string Ecosystem);
```

**Rule of thumb:** If you're unsure whether something belongs in SharedKernel, it probably doesn't. Start with it in a module and promote later if multiple modules genuinely need it.

### Host Projects

Hosts are thin — they install modules and configure middleware. No business logic.

```csharp
// PVT.Host.Web/Program.cs
var builder = WebApplication.CreateBuilder(args);

// Module installation
builder.InstallModule<IdentityModuleInstaller>();
builder.InstallModule<ClientsModuleInstaller>();
builder.InstallModule<RepositoriesModuleInstaller>();
builder.InstallModule<ScanningModuleInstaller>();
builder.InstallModule<VulnerabilitiesModuleInstaller>();
builder.InstallModule<AuditingModuleInstaller>();

// Cross-cutting middleware (auth, compression, antiforgery)
builder.ConfigureAuthentication();
builder.ConfigureResponseCompression();
builder.Services.AddRazorComponents();

var app = builder.Build();

app.UseForwardedHeaders();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Each module maps its own endpoints
app.MapModuleEndpoints<IdentityModuleInstaller>();
app.MapModuleEndpoints<ClientsModuleInstaller>();
app.MapModuleEndpoints<RepositoriesModuleInstaller>();
app.MapModuleEndpoints<ScanningModuleInstaller>();
app.MapModuleEndpoints<VulnerabilitiesModuleInstaller>();
app.MapModuleEndpoints<AuditingModuleInstaller>();

app.MapRazorComponents<App>();

app.Run();
```

```csharp
// PVT.Host.Worker/Program.cs
var builder = Host.CreateApplicationBuilder(args);

// Only install modules the worker needs
builder.InstallModule<ScanningModuleInstaller>();
builder.InstallModule<RepositoriesModuleInstaller>();   // Scanning depends on this
builder.InstallModule<VulnerabilitiesModuleInstaller>(); // Scanning triggers this
builder.InstallModule<AuditingModuleInstaller>();        // Logging

builder.Services.AddHostedService<ScanningWorkerService>();

var host = builder.Build();
await host.RunAsync();
```

### Database Strategy

All modules share one PostgreSQL database but use **separate schemas** and **separate DbContexts**:

```
┌─────────────────────────────────────────────────────────┐
│                    PostgreSQL Database                    │
│                                                          │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐     │
│  │  identity    │  │  clients    │  │  repos       │     │
│  │  schema      │  │  schema     │  │  schema      │     │
│  │             │  │             │  │             │     │
│  │  users      │  │  clients    │  │  repositories│     │
│  │  roles      │  │             │  │  repo_scans  │     │
│  │  user_roles │  │             │  │             │     │
│  └─────────────┘  └─────────────┘  └─────────────┘     │
│                                                          │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐     │
│  │  scanning   │  │  vulns      │  │  auditing    │     │
│  │  schema     │  │  schema     │  │  schema      │     │
│  │             │  │             │  │             │     │
│  │  pkg_files  │  │  vulns      │  │  audit_logs  │     │
│  │  packages   │  │  vuln_src   │  │  error_logs  │     │
│  │  supported  │  │             │  │  notifs      │     │
│  └─────────────┘  └─────────────┘  └─────────────┘     │
│                                                          │
└─────────────────────────────────────────────────────────┘
```

```csharp
// Each module's DbContext specifies its schema
public class ScanningDbContext : DbContext
{
    public DbSet<RepositoryPackageFile> PackageFiles => Set<RepositoryPackageFile>();
    public DbSet<RepositoryPackage> Packages => Set<RepositoryPackage>();
    public DbSet<SupportedPackageFile> SupportedFiles => Set<SupportedPackageFile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("scanning");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ScanningDbContext).Assembly);
    }
}

// Each module manages its own migrations
// dotnet ef migrations add InitialScanning --context ScanningDbContext --output-dir Infrastructure/Migrations
```

**Foreign keys across schemas:** Where a table needs to reference another module's entity (e.g., `repositories.repositories.client_id` → `clients.clients.id`), use a raw foreign key column without a navigation property. The owning module treats it as an opaque integer.

```csharp
// In Repositories module — references Client by ID, not by navigation
public class Repository : Entity
{
    public int ClientId { get; set; }       // FK to clients.clients.id
    // No: public Client Client { get; set; }  — Client is owned by another module
    public string CanonicalName { get; set; }
    public SourceControlProvider Provider { get; set; }
}
```

---

## Migration Guide

### Prerequisites

- All 20 issues from the [Architectural Issues doc](./architectural-issues.md) should be reviewed (not all need to be fixed first, but understanding them is important)
- Each phase should result in a deployable, working application
- Write tests as you go — each module should have its own test project from the start

### Phase 0: Foundation

**Goal:** Prepare the codebase for modular extraction without changing behavior.

**Steps:**

1. **Create the `docs/` and `src/` directory structure**
   ```
   mkdir src
   mkdir src/Modules
   mkdir tests
   ```

2. **Move existing projects under `src/`** (optional — keeps things clean but requires updating all paths)

3. **Fix the test project namespace** — change `ClientBooking.Tests` to `PVT.Tests` in the `.csproj`

4. **Re-enable the CI test job** — uncomment the test step in `.github/workflows/pipeline.yml`

5. **Add an architecture decision record (ADR)** documenting the move to modular monolith and why

**Deliverable:** Same application, same behavior, cleaner foundation.

---

### Phase 1: Extract SharedKernel

**Goal:** Create `PVT.SharedKernel` with types that are genuinely shared across all modules.

**Steps:**

1. Create `PVT.SharedKernel` project:
   ```bash
   dotnet new classlib -n PVT.SharedKernel -f net10.0
   ```

2. Move these types from their current locations:

   | Type | Current Location | New Location |
   |------|-----------------|-------------|
   | `Entity` | `PVT.Domain/Entities/Entity.cs` | `PVT.SharedKernel/Domain/Entity.cs` |
   | `Result`, `Result<T>` | `PVT.Application/Common/Result.cs` | `PVT.SharedKernel/Application/Result.cs` |
   | `PagedQuery` | `PVT.Application/Common/` | `PVT.SharedKernel/Application/PagedQuery.cs` |
   | `PagedList<T>` | `PVT.Application/Common/` | `PVT.SharedKernel/Application/PagedList.cs` |
   | `Pagination` | `PVT.Application/Common/` | `PVT.SharedKernel/Application/Pagination.cs` |
   | `SortDirection` | `PVT.Domain/Shared/` | `PVT.SharedKernel/Domain/SortDirection.cs` |
   | `SourceControlProvider` | `PVT.Domain/Shared/` | `PVT.SharedKernel/Domain/SourceControlProvider.cs` |
   | `PackageFileType` | `PVT.Domain/Shared/` | `PVT.SharedKernel/Domain/PackageFileType.cs` |

3. Add `IModuleInstaller` interface:
   ```csharp
   public interface IModuleInstaller
   {
       void Install(IServiceCollection services, IConfiguration configuration);
       void MapEndpoints(IEndpointRouteBuilder app);
   }
   ```

4. Add extension methods for module registration:
   ```csharp
   public static class ModuleExtensions
   {
       public static void InstallModule<T>(this WebApplicationBuilder builder)
           where T : IModuleInstaller, new()
       {
           var installer = new T();
           installer.Install(builder.Services, builder.Configuration);
       }

       public static void MapModuleEndpoints<T>(this WebApplication app)
           where T : IModuleInstaller, new()
       {
           var installer = new T();
           installer.MapEndpoints(app);
       }
   }
   ```

5. Update all existing projects to reference `PVT.SharedKernel` instead of each other for these types.

6. Verify the application still builds and runs.

**Deliverable:** SharedKernel project with shared types. All existing projects reference it. Zero behavior change.

---

### Phase 2: Extract Identity Module

**Goal:** Extract User, Role, auth, and login into a self-contained module. This is the easiest module to extract because it has the fewest inbound dependencies.

**Steps:**

1. Create `PVT.Modules.Identity` project:
   ```bash
   dotnet new classlib -n PVT.Modules.Identity -f net10.0
   ```

2. Create the `Contracts/` folder with the public API:
   ```csharp
   // Contracts/IIdentityModule.cs
   public interface IIdentityModule
   {
       Task<UserSummaryDto?> GetUserById(int id);
       Task<bool> IsUserActive(int userId);
   }

   // Contracts/ICurrentUserService.cs
   public interface ICurrentUserService
   {
       int? GetCurrentUserId();
   }
   ```

3. Move entity classes:
   - `PVT.Domain/Entities/User.cs` → `Modules/Identity/Domain/User.cs`
   - `PVT.Domain/Entities/Role.cs` → `Modules/Identity/Domain/Role.cs`
   - `PVT.Domain/Entities/UserRole.cs` → `Modules/Identity/Domain/UserRole.cs`
   - `PVT.Domain/UserLoginService.cs` → `Modules/Identity/Domain/UserLoginService.cs`

4. Create `IdentityDbContext`:
   ```csharp
   internal class IdentityDbContext : DbContext
   {
       public DbSet<User> Users => Set<User>();
       public DbSet<Role> Roles => Set<Role>();

       protected override void OnModelCreating(ModelBuilder modelBuilder)
       {
           modelBuilder.HasDefaultSchema("identity");
           // Move User/Role configuration from DataContext to here
       }
   }
   ```

5. Move repository implementations:
   - `UserRepository` → `Modules/Identity/Infrastructure/Repositories/`
   - `UserQueryRepository` → `Modules/Identity/Infrastructure/Repositories/`
   - `RoleRepository` → `Modules/Identity/Infrastructure/Repositories/`

6. Move handlers:
   - `UserCreateHandler`, `UserEditHandler`, `UserDeleteHandler`, `UserSearchHandler`
   - `LoginHandler`, `LogoutHandler`, `ToggleAdminHandler`

7. Move endpoints:
   - `LoginEndpoint`, `LogoutEndpoint`, `UserCreateEndpoint`, `UserEditEndpoint`, etc.

8. Move authentication services:
   - `AspNetSessionStateManager` → `Modules/Identity/Infrastructure/Services/`
   - `AspNetPasswordService` → `Modules/Identity/Infrastructure/Services/`

9. Create the module installer:
   ```csharp
   public class IdentityModuleInstaller : IModuleInstaller
   {
       public void Install(IServiceCollection services, IConfiguration configuration)
       {
           services.AddDbContext<IdentityDbContext>(options =>
               options.UseNpgsql(/* connection string */));

           services.AddScoped<IIdentityModule, IdentityModuleFacade>();
           services.AddScoped<ICurrentUserService, HttpCurrentUserService>();

           // Register all handlers, validators, repositories internal to this module
       }

       public void MapEndpoints(IEndpointRouteBuilder app)
       {
           LoginEndpoint.MapEndpoint(app);
           LogoutEndpoint.MapEndpoint(app);
           UserCreateEndpoint.MapEndpoint(app);
           // ...
       }
   }
   ```

10. Remove the moved types from `PVT.Domain`, `PVT.Application`, and `PVT.Infrastructure`.

11. Update other projects that referenced `User` or `Role` to use the module's contracts instead.

12. Write tests for the Identity module handlers.

**Deliverable:** Identity module is self-contained. Other modules use `IIdentityModule` and `ICurrentUserService` contracts instead of directly accessing User/Role entities.

---

### Phase 3: Extract Clients Module

**Goal:** Extract Client entity and all CRUD operations.

**Steps:**

1. Create `PVT.Modules.Clients` project

2. Define contracts:
   ```csharp
   public interface IClientModule
   {
       Task<ClientSummaryDto?> GetClientById(int id);
       Task<bool> ClientExists(int clientId);
   }
   ```

3. Move: `Client` entity, `ClientRepository`, `ClientQueryRepository`, all Client handlers/validators/endpoints

4. Create `ClientsDbContext` with `clients` schema

5. Remove `Client`-related code from the old projects

6. The `Repository` entity (in the Repositories module) will reference `ClientId` as an opaque int — no navigation property to `Client`

**Deliverable:** Client module is self-contained. Repositories module references clients by ID only.

---

### Phase 4: Extract Auditing Module

**Goal:** Extract audit logs, error logs, notifications, and the logging infrastructure.

**Steps:**

1. Create `PVT.Modules.Auditing` project

2. Move: `AuditLog`, `ErrorLog`, `Notification`, `UserNotification`, `AuditInterceptor`, `DatabaseLoggerProvider`, `DatabaseLogger`

3. Create `AuditingDbContext` with `auditing` schema

4. The `AuditInterceptor` becomes a shared interceptor that other module DbContexts can opt into via their installers:
   ```csharp
   // In AuditingModuleInstaller
   public void Install(IServiceCollection services, IConfiguration configuration)
   {
       services.AddScoped<AuditSaveChangesInterceptor>();
       // Other modules add this interceptor to their DbContext if they want auditing
   }
   ```

5. Move audit/error log endpoints

**Deliverable:** Auditing is a standalone module. Other modules opt into auditing by adding the interceptor.

---

### Phase 5: Extract Repositories Module

**Goal:** Extract Repository and RepositoryScan entities and management operations.

**Steps:**

1. Create `PVT.Modules.Repositories` project

2. Define contracts (this is the most important contract — Scanning depends on it):
   ```csharp
   public interface IRepositoryModule
   {
       Task<List<RepositoryScanDueDto>> GetRepositoriesDueForScan();
       Task UpdateLastTrackedCommit(int repositoryId, string commitHash);
       Task<RepositorySummaryDto?> GetById(int id);
       Task RecordScan(int repositoryId, string commitHash, DateTimeOffset completedAt);
   }
   ```

3. Move: `Repository`, `RepositoryScan` entities, repositories, handlers, endpoints

4. Create `RepositoriesDbContext` with `repos` schema

5. The `ClientId` column remains as an opaque FK — no navigation to `Client`

**Deliverable:** Repository module is self-contained. Scanning module uses `IRepositoryModule` contract.

---

### Phase 6: Extract Scanning Module

**Goal:** Extract the core scanning pipeline, provider strategies, and file parsers. This is the most complex extraction.

**Steps:**

1. Create `PVT.Modules.Scanning` project

2. Move all scanning-related types:
   - `RepositoryPackageFile`, `RepositoryPackage`, `SupportedPackageFile` entities
   - All strategy interfaces and implementations (`GithubScanStrategy`, `BitbucketScanStrategy`, `CsProjParser`, `PackageJsonParser`)
   - All scanning handlers (`RepositoryChangeTrackingHandler`, `RepositoryScanHandler`, `PackageFileParsingHandler`)
   - GitHub and Bitbucket API clients

3. Create `ScanningDbContext` with `scanning` schema

4. Replace direct repository access with module contracts:
   ```csharp
   // BEFORE: Handler directly injects IRepositoryRepository
   public class RepositoryScanHandler(IRepositoryRepository repo) { ... }

   // AFTER: Handler uses IRepositoryModule contract
   public class FileScanStep(IRepositoryModule repositoryModule)
   {
       public async Task Execute()
       {
           var repos = await repositoryModule.GetRepositoriesDueForScan();
           // Process each repo...
       }
   }
   ```

5. Publish integration events when packages are discovered:
   ```csharp
   // After parsing package files, publish an event for the Vulnerabilities module
   await eventBus.Publish(new PackagesDiscoveredEvent(
       repositoryId,
       discoveredPackages,
       DateTimeOffset.UtcNow));
   ```

6. Move scanning-related configuration (`GithubConfiguration`, `BitbucketConfiguration`) into the module

**Deliverable:** Scanning module is self-contained. Communicates with Repositories via contracts, triggers Vulnerability lookups via events.

---

### Phase 7: Extract Vulnerabilities Module

**Goal:** Extract vulnerability entities, OSV database integration, and CVSS scoring.

**Steps:**

1. Create `PVT.Modules.Vulnerabilities` project

2. Move: `Vulnerability`, `VulnerabilitySource` entities, `OsvDatabaseStrategy`, `CvssCalculatorService`

3. Create `VulnerabilitiesDbContext` with `vulns` schema

4. Subscribe to `PackagesDiscoveredEvent` from the Scanning module:
   ```csharp
   public class VulnerabilityLookupEventHandler : IIntegrationEventHandler<PackagesDiscoveredEvent>
   {
       public async Task Handle(PackagesDiscoveredEvent @event)
       {
           foreach (var package in @event.Packages)
           {
               await LookupAndStoreVulnerabilities(package);
           }
       }
   }
   ```

5. Move vulnerability detail endpoints

**Deliverable:** Vulnerabilities module is self-contained. Triggered by events from Scanning.

---

### Phase 8: Cleanup & Validation

**Goal:** Remove the old projects, validate boundaries, add architecture tests.

**Steps:**

1. **Delete old projects:** Remove `PVT.Domain`, `PVT.Application`, `PVT.Infrastructure` once all code has been migrated

2. **Rename hosts:** `PVT.Web` → `PVT.Host.Web`, `PVT.Worker` → `PVT.Host.Worker`

3. **Update solution file** to reflect new project structure

4. **Update Docker files** and CI/CD pipeline for new paths

5. **Add architecture tests** to enforce boundaries:
   ```csharp
   [Fact]
   public void Scanning_Module_Should_Not_Reference_Clients_Internals()
   {
       var scanningAssembly = typeof(ScanningModuleInstaller).Assembly;
       var referencedAssemblies = scanningAssembly.GetReferencedAssemblies();

       // Scanning can reference Clients.Contracts (via the shared project)
       // but should never reference Clients' internal types
       Assert.DoesNotContain(referencedAssemblies,
           a => a.Name == "PVT.Modules.Clients");
   }

   [Fact]
   public void Module_Internal_Types_Should_Not_Be_Public()
   {
       var modules = new[]
       {
           typeof(ScanningModuleInstaller).Assembly,
           typeof(IdentityModuleInstaller).Assembly,
           typeof(ClientsModuleInstaller).Assembly,
       };

       foreach (var module in modules)
       {
           var publicNonContractTypes = module.GetExportedTypes()
               .Where(t => !t.Namespace!.Contains("Contracts")
                   && !t.Name.EndsWith("ModuleInstaller")
                   && !t.Name.EndsWith("Endpoint"))
               .ToList();

           Assert.Empty(publicNonContractTypes);
       }
   }
   ```

6. **Run full integration test suite** to verify all module interactions work correctly

7. **Update documentation** — README, deployment guides, developer onboarding

**Deliverable:** Clean modular monolith with enforced boundaries, full test coverage per module, and architecture tests preventing regression.

---

## Key Implementation Patterns

### Module Installer Contract

Every module exposes exactly one public installer class:

```csharp
// PVT.SharedKernel/Infrastructure/IModuleInstaller.cs
public interface IModuleInstaller
{
    /// <summary>
    /// Register all services, DbContext, repositories, handlers, and validators
    /// internal to this module.
    /// </summary>
    void Install(IServiceCollection services, IConfiguration configuration);

    /// <summary>
    /// Map all HTTP endpoints owned by this module.
    /// </summary>
    void MapEndpoints(IEndpointRouteBuilder app);
}

// PVT.SharedKernel/Infrastructure/ModuleExtensions.cs
public static class ModuleExtensions
{
    private static readonly List<IModuleInstaller> InstalledModules = [];

    public static void InstallModule<T>(this WebApplicationBuilder builder)
        where T : IModuleInstaller, new()
    {
        var installer = new T();
        installer.Install(builder.Services, builder.Configuration);
        InstalledModules.Add(installer);
    }

    // Overload for Host (Worker)
    public static void InstallModule<T>(this HostApplicationBuilder builder)
        where T : IModuleInstaller, new()
    {
        var installer = new T();
        installer.Install(builder.Services, builder.Configuration);
        InstalledModules.Add(installer);
    }

    public static void MapAllModuleEndpoints(this WebApplication app)
    {
        foreach (var module in InstalledModules)
        {
            module.MapEndpoints(app);
        }
    }
}
```

```csharp
// Example: PVT.Modules.Clients/ClientsModuleInstaller.cs
public class ClientsModuleInstaller : IModuleInstaller
{
    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // DbContext
        services.AddDbContext<ClientsDbContext>((sp, options) =>
        {
            var settings = sp.GetRequiredService<IOptions<DatabaseSettings>>().Value;
            options.UseNpgsql(settings.ToConnectionString());
        });

        // Public contract
        services.AddScoped<IClientModule, ClientModuleFacade>();

        // Internal services
        services.AddScoped<ClientCreateHandler>();
        services.AddScoped<ClientEditHandler>();
        services.AddScoped<ClientDeleteHandler>();
        services.AddScoped<ClientSearchHandler>();
        services.AddScoped<ClientDetailsHandler>();
        services.AddScoped<ClientCreateValidator>();
        services.AddScoped<ClientEditValidator>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ClientCreateEndpoint.MapEndpoint(app);
        ClientEditEndpoint.MapEndpoint(app);
        ClientDeleteEndpoint.MapEndpoint(app);
        ClientSearchEndpoint.MapEndpoint(app);
        ClientDetailsEndpoint.MapEndpoint(app);
    }
}
```

### Module Contracts (Public API)

Contracts are the only way modules communicate. They live in the module's `Contracts/` folder and use DTOs, never domain entities.

```csharp
// PVT.Modules.Repositories/Contracts/IRepositoryModule.cs
public interface IRepositoryModule
{
    /// <summary>
    /// Get repositories that are due for scanning based on their scan interval
    /// and last tracked timestamp.
    /// </summary>
    Task<List<RepositoryScanDueDto>> GetRepositoriesDueForScan();

    /// <summary>
    /// Update the last tracked commit hash after change detection.
    /// </summary>
    Task UpdateLastTrackedCommit(int repositoryId, string commitHash);

    /// <summary>
    /// Record that a scan was completed for a repository.
    /// </summary>
    Task RecordScanCompleted(int repositoryId, string commitHash);
}

// PVT.Modules.Repositories/Contracts/Dtos/RepositoryScanDueDto.cs
public sealed record RepositoryScanDueDto(
    int RepositoryId,
    string CanonicalName,
    SourceControlProvider Provider,
    string? ExternalRepositoryId,
    string? ExternalDefaultBranch,
    string? LastTrackedCommitHash,
    string? ExternalUrl);
```

```csharp
// Implementation is INTERNAL to the module
internal class RepositoryModuleFacade(RepositoriesDbContext db) : IRepositoryModule
{
    public async Task<List<RepositoryScanDueDto>> GetRepositoriesDueForScan()
    {
        return await db.Repositories
            .Where(r => r.IsActive && r.NextScanDueAt <= DateTimeOffset.UtcNow)
            .Select(r => new RepositoryScanDueDto(
                r.Id, r.CanonicalName, r.Provider,
                r.ExternalRepositoryId, r.ExternalDefaultBranch,
                r.LastTrackedCommitHash, r.ExternalUrl))
            .ToListAsync();
    }
}
```

### Integration Events

For fire-and-forget communication (e.g. "packages were discovered, someone should check for vulnerabilities"), use an in-process event bus.

```csharp
// PVT.SharedKernel/IntegrationEvents/IIntegrationEventBus.cs
public interface IIntegrationEventBus
{
    Task Publish<T>(T @event, CancellationToken ct = default) where T : IIntegrationEvent;
}

public interface IIntegrationEvent
{
    DateTimeOffset OccurredAt { get; }
}

public interface IIntegrationEventHandler<in T> where T : IIntegrationEvent
{
    Task Handle(T @event, CancellationToken ct = default);
}

// Simple in-process implementation using DI
internal class InProcessEventBus(IServiceProvider serviceProvider) : IIntegrationEventBus
{
    public async Task Publish<T>(T @event, CancellationToken ct = default) where T : IIntegrationEvent
    {
        using var scope = serviceProvider.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IIntegrationEventHandler<T>>();

        foreach (var handler in handlers)
        {
            try
            {
                await handler.Handle(@event, ct);
            }
            catch (Exception ex)
            {
                // Log but don't let one handler failure break others
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<InProcessEventBus>>();
                logger.LogError(ex, "Event handler {Handler} failed for {Event}",
                    handler.GetType().Name, typeof(T).Name);
            }
        }
    }
}
```

```csharp
// Publishing (in Scanning module)
internal class PackageParseStep(IIntegrationEventBus eventBus)
{
    public async Task Execute(int repositoryId, List<ParsedPackage> packages)
    {
        // ... save packages to ScanningDbContext ...

        await eventBus.Publish(new PackagesDiscoveredEvent(
            repositoryId,
            packages.Select(p => new DiscoveredPackage(p.Name, p.Version, p.Ecosystem)).ToList(),
            DateTimeOffset.UtcNow));
    }
}

// Subscribing (in Vulnerabilities module)
internal class VulnerabilityLookupOnPackagesDiscovered(
    IOsvClient osvClient,
    VulnerabilitiesDbContext db)
    : IIntegrationEventHandler<PackagesDiscoveredEvent>
{
    public async Task Handle(PackagesDiscoveredEvent @event, CancellationToken ct)
    {
        foreach (var package in @event.Packages)
        {
            var vulns = await osvClient.Query(package.PackageName, package.PackageVersion, package.Ecosystem);
            // Store results...
        }
    }
}

// Registration (in VulnerabilitiesModuleInstaller)
services.AddScoped<IIntegrationEventHandler<PackagesDiscoveredEvent>,
    VulnerabilityLookupOnPackagesDiscovered>();
```

### Per-Module DbContext

Each module owns a DbContext scoped to its schema:

```csharp
// PVT.Modules.Scanning/Infrastructure/ScanningDbContext.cs
internal class ScanningDbContext(DbContextOptions<ScanningDbContext> options) : DbContext(options)
{
    public DbSet<RepositoryPackageFile> PackageFiles => Set<RepositoryPackageFile>();
    public DbSet<RepositoryPackage> Packages => Set<RepositoryPackage>();
    public DbSet<SupportedPackageFile> SupportedFiles => Set<SupportedPackageFile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("scanning");

        modelBuilder.Entity<RepositoryPackageFile>(entity =>
        {
            entity.ToTable("package_files");
            entity.HasKey(e => e.Id);

            // FK to another module's table — raw column, no navigation
            entity.Property(e => e.RepositoryScanId);
            // NO: entity.HasOne(e => e.RepositoryScan)
        });

        modelBuilder.Entity<RepositoryPackage>(entity =>
        {
            entity.ToTable("packages");
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.PackageFile)
                .WithMany(f => f.Packages)
                .HasForeignKey(e => e.PackageFileId);
        });
    }
}
```

**Migrations:** Each module manages its own EF Core migrations independently:

```bash
# Generate migration for a specific module
cd src/Modules/PVT.Modules.Scanning
dotnet ef migrations add InitialScanning \
    --context ScanningDbContext \
    --output-dir Infrastructure/Migrations \
    --startup-project ../../PVT.Host.Web
```

### Architecture Tests

Enforce module boundaries with automated tests:

```csharp
// tests/PVT.ArchitectureTests/ModuleBoundaryTests.cs
public class ModuleBoundaryTests
{
    private static readonly Assembly[] ModuleAssemblies =
    [
        typeof(IdentityModuleInstaller).Assembly,
        typeof(ClientsModuleInstaller).Assembly,
        typeof(RepositoriesModuleInstaller).Assembly,
        typeof(ScanningModuleInstaller).Assembly,
        typeof(VulnerabilitiesModuleInstaller).Assembly,
        typeof(AuditingModuleInstaller).Assembly,
    ];

    [Fact]
    public void Modules_Should_Not_Reference_Other_Module_Internals()
    {
        foreach (var module in ModuleAssemblies)
        {
            var moduleName = module.GetName().Name!;
            var referencedModules = module.GetReferencedAssemblies()
                .Where(a => a.Name!.StartsWith("PVT.Modules.")
                    && a.Name != moduleName)
                .ToList();

            // Modules should NOT directly reference other module assemblies.
            // They communicate via SharedKernel contracts only.
            Assert.Empty(referencedModules);
        }
    }

    [Fact]
    public void SharedKernel_Should_Not_Reference_Any_Module()
    {
        var sharedKernel = typeof(Entity).Assembly;
        var moduleReferences = sharedKernel.GetReferencedAssemblies()
            .Where(a => a.Name!.StartsWith("PVT.Modules."))
            .ToList();

        Assert.Empty(moduleReferences);
    }

    [Fact]
    public void Host_Projects_Should_Not_Contain_Business_Logic()
    {
        var webHost = typeof(Program).Assembly;
        var handlerTypes = webHost.GetTypes()
            .Where(t => t.Name.EndsWith("Handler") && t.IsClass)
            .ToList();

        Assert.Empty(handlerTypes);
    }

    [Fact]
    public void Each_Module_Should_Have_Exactly_One_DbContext()
    {
        foreach (var module in ModuleAssemblies)
        {
            var dbContextTypes = module.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(DbContext)))
                .ToList();

            Assert.Single(dbContextTypes);
        }
    }

    [Fact]
    public void Module_Domain_Entities_Should_Not_Have_Navigation_Properties_To_Other_Modules()
    {
        foreach (var module in ModuleAssemblies)
        {
            var moduleName = module.GetName().Name!;
            var entityTypes = module.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(Entity)))
                .ToList();

            foreach (var entity in entityTypes)
            {
                var navigationProperties = entity.GetProperties()
                    .Where(p => p.PropertyType.IsSubclassOf(typeof(Entity))
                        && p.PropertyType.Assembly.GetName().Name != moduleName)
                    .ToList();

                Assert.Empty(navigationProperties);
            }
        }
    }
}
```

---

## Summary: Migration Effort Estimate

| Phase | Scope | Risk |
|-------|-------|------|
| Phase 0: Foundation | Directory restructure, test fix, CI fix | Minimal |
| Phase 1: SharedKernel | Extract shared types, update references | Low |
| Phase 2: Identity | Extract Users, Roles, Auth — fewest dependents | Low-Medium |
| Phase 3: Clients | Extract Clients — simple CRUD, one FK from Repositories | Low |
| Phase 4: Auditing | Extract logs, notifications, interceptor | Medium |
| Phase 5: Repositories | Extract Repositories — scanning depends on this | Medium |
| Phase 6: Scanning | Extract pipeline, strategies, parsers — most complex | High |
| Phase 7: Vulnerabilities | Extract vulns, OSV client — triggered by scanning | Medium |
| Phase 8: Cleanup | Delete old projects, add arch tests, update CI/CD | Low |

**Key risk:** Phase 6 (Scanning) is the most complex because it currently has the deepest cross-cutting dependencies — it touches Repositories, PackageFiles, Packages, and triggers Vulnerability lookups. Extract it last, after all its dependencies are already modularised.

**Key principle:** The application must be deployable and fully functional after every phase. Never leave the codebase in a half-migrated state.
