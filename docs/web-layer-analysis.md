# PVT.Web Layer Analysis: Patterns, Strengths & Scalable HTMX Architecture

> **Project:** Propel Vulnerability Tracker
> **Date:** 2026-02-09
> **Scope:** Deep analysis of the PVT.Web project — endpoint architecture, Razor component system, HTMX integration patterns, and recommendations for scaling

---

## Table of Contents

- [Executive Summary](#executive-summary)
- [Architecture Overview](#architecture-overview)
  - [Request Lifecycle](#request-lifecycle)
  - [Project Layout](#project-layout)
- [What Is Built Well](#what-is-built-well)
  - [1. The IEndpoint Static Abstract Pattern](#1-the-iendpoint-static-abstract-pattern)
  - [2. Centralised Route Constants With Extension Methods](#2-centralised-route-constants-with-extension-methods)
  - [3. Custom IResult Types for HTMX](#3-custom-iresult-types-for-htmx)
  - [4. The Generic DataGrid Component System](#4-the-generic-data-grid-component-system)
  - [5. Out-of-Band Toast Notification System](#5-out-of-band-toast-notification-system)
  - [6. Confirmation Modal Pattern](#6-confirmation-modal-pattern)
  - [7. ClientNavigationFilter for Browser History](#7-clientnavigationfilter-for-browser-history)
  - [8. Dual-Route Endpoints (GET Page + POST Action)](#8-dual-route-endpoints-get-page--post-action)
  - [9. Fluent Endpoint Registration Chain](#9-fluent-endpoint-registration-chain)
  - [10. PageLayout as the HTMX Orchestration Hub](#10-pagelayout-as-the-htmx-orchestration-hub)
- [HTMX Patterns Catalogue](#htmx-patterns-catalogue)
  - [Pattern 1: Full Page Content Swap](#pattern-1-full-page-content-swap)
  - [Pattern 2: Debounced Search](#pattern-2-debounced-search)
  - [Pattern 3: Stateful Pagination](#pattern-3-stateful-pagination)
  - [Pattern 4: Column Sorting With State Preservation](#pattern-4-column-sorting-with-state-preservation)
  - [Pattern 5: Out-of-Band (OOB) Swap for Toasts](#pattern-5-out-of-band-oob-swap-for-toasts)
  - [Pattern 6: OOB Swap for Page Metadata](#pattern-6-oob-swap-for-page-metadata)
  - [Pattern 7: Modal Injection](#pattern-7-modal-injection)
  - [Pattern 8: Delete With Row Removal](#pattern-8-delete-with-row-removal)
  - [Pattern 9: Retarget on Error](#pattern-9-retarget-on-error)
  - [Pattern 10: Server-Side Redirect via Header](#pattern-10-server-side-redirect-via-header)
  - [Pattern 11: Polling for Notifications](#pattern-11-polling-for-notifications)
  - [Pattern 12: Browser History Management](#pattern-12-browser-history-management)
  - [Pattern 13: Progressive Form Wizards](#pattern-13-progressive-form-wizards)
  - [Pattern 14: In-Place Badge Replacement](#pattern-14-in-place-badge-replacement)
- [Areas for Improvement](#areas-for-improvement)
  - [1. Repeated hx-include Attribute Strings](#1-repeated-hx-include-attribute-strings)
  - [2. No Client-Side Validation](#2-no-client-side-validation)
  - [3. Inconsistent Error Response Targets](#3-inconsistent-error-response-targets)
  - [4. Missing Return in RepositoryDetailsEndpoint](#4-missing-return-in-repositorydetailsendpoint)
  - [5. Component Location Inconsistency](#5-component-location-inconsistency)
  - [6. No Loading Indicators](#6-no-loading-indicators)
  - [7. Inline onclick Handlers in Modals](#7-inline-onclick-handlers-in-modals)
  - [8. Tailwind Rebuilds on Every Debug Compile](#8-tailwind-rebuilds-on-every-debug-compile)
- [Scaling These Patterns: A Repeatable HTMX Component Framework](#scaling-these-patterns-a-repeatable-htmx-component-framework)
  - [Formalising the DataGrid as a Convention](#formalising-the-datagrid-as-a-convention)
  - [A Generic CRUD Endpoint Base](#a-generic-crud-endpoint-base)
  - [Standardised Error Response Pipeline](#standardised-error-response-pipeline)
  - [Loading State Pattern](#loading-state-pattern)
  - [Optimistic UI Pattern](#optimistic-ui-pattern)
  - [Infinite Scroll Pattern](#infinite-scroll-pattern)
  - [Server-Sent Events (SSE) for Real-Time Updates](#server-sent-events-sse-for-real-time-updates)
  - [Confirmation Pattern as a Convention](#confirmation-pattern-as-a-convention)
  - [Form Wizard Pattern](#form-wizard-pattern)

---

## Executive Summary

PVT.Web is a server-rendered ASP.NET Core application that uses **Razor Components + HTMX** instead of a JavaScript SPA framework. This is a deliberate architectural choice that delivers SPA-like interactivity (partial page updates, modals, toast notifications, browser history management) without client-side routing, a JavaScript build pipeline, or a client-side state management library.

The implementation is notably well-executed. The codebase establishes several strong patterns — a static abstract `IEndpoint` interface, custom `IResult` types that speak HTMX's header protocol, a generic DataGrid component system, and out-of-band toast notifications — that together form a cohesive, repeatable architecture.

This document catalogues what works well, identifies the specific HTMX patterns in use, notes areas that could be tightened, and proposes how to scale these patterns into a formal framework for building new features rapidly.

---

## Architecture Overview

### Request Lifecycle

```
Browser                          ASP.NET Core                    Handler Layer
───────                          ────────────                    ─────────────

 User clicks link/submits form
        │
        ▼
 HTMX intercepts event
 (hx-get, hx-post, hx-delete)
        │
        ▼
 HTMX sends AJAX request         ┌─────────────────────┐
 with headers:                    │  Middleware Pipeline  │
 HX-Request: true          ──►   │                       │
 HX-Target: #target-id           │  Auth → Antiforgery   │
 HX-Current-URL: /page           │  → Routing → Endpoint │
                                  └──────────┬────────────┘
                                             │
                                  ┌──────────▼────────────┐
                                  │   IEndpoint Handler    │
                                  │                        │
                                  │  1. Bind [FromForm]    │──► Application Handler
                                  │  2. Call handler       │──► Validator
                                  │  3. Pattern match      │──► Repository
                                  │     on Result          │
                                  └──────────┬────────────┘
                                             │
                             ┌───────────────┼───────────────┐
                             │               │               │
                     Success Path      Validation Error   Business Error
                             │               │               │
                     HtmxRedirect      Component<Form>   Component<Toast>
                     (HX-Redirect      with errors &     or Retarget to
                      header)          original input    #toast-container
                             │               │               │
                             ▼               ▼               ▼
                                  ┌──────────────────────────┐
                                  │  ClientNavigationFilter   │
                                  │  Adds HX-Push-Url header  │
                                  │  (strips /api prefix)     │
                                  └──────────┬───────────────┘
                                             │
 HTMX processes response    ◄───────────────┘
        │
        ├─► HX-Redirect?     → Full page navigation
        ├─► HX-Retarget?     → Swap into different element
        ├─► HX-Push-Url?     → Update browser URL bar
        ├─► hx-swap-oob?     → Out-of-band swap (toast, metadata)
        └─► Default           → Swap innerHTML of hx-target
```

### Project Layout

```
PVT.Web/
├── Application/                        ◄── Endpoint layer
│   ├── IEndpoint.cs                        Static abstract interface
│   ├── AppRoutes.cs                        All route constants + helpers
│   ├── Filters/
│   │   └── ClientNavigationFilter.cs       HX-Push-Url endpoint filter
│   ├── Results/
│   │   ├── HtmxRedirectResult.cs           HX-Redirect IResult
│   │   ├── HtmxRefreshResult.cs            HX-Refresh IResult
│   │   ├── HtmxRetargetResult.cs           HX-Retarget + HX-Reswap IResult
│   │   └── HtmxResultExtensions.cs         Builder methods
│   └── Features/
│       ├── Clients/                        Feature-sliced endpoints + components
│       │   ├── Create/
│       │   │   ├── ClientCreateEndpoint.cs
│       │   │   └── (no component — uses shared ClientForm)
│       │   ├── Edit/
│       │   ├── Delete/
│       │   ├── Search/
│       │   │   ├── ClientSearchEndpoint.cs
│       │   │   ├── ClientsSearchTemplate.razor
│       │   │   └── ClientsSearchGrid.razor
│       │   ├── Details/
│       │   └── Shared/
│       │       └── ClientForm.razor
│       ├── Users/
│       ├── Repositories/
│       ├── Login/
│       ├── Home/
│       └── ...
│
├── Components/                         ◄── Reusable UI layer
│   ├── App.razor                           Root document (loads HTMX)
│   ├── Routes.razor                        Router config
│   ├── Layout/
│   │   ├── SidebarLayout.razor             Main authenticated layout
│   │   ├── PageLayout.razor                Content wrapper (hx targets)
│   │   ├── SidebarComponent.razor          Nav with HTMX links
│   │   ├── TopBarComponent.razor           Sticky header
│   │   ├── TopBarMetaData.razor            OOB page title swap
│   │   └── LoginLayout.razor               Unauthenticated layout
│   ├── Generic/
│   │   ├── DataGrid/                       Composable table system
│   │   │   ├── DataGrid.razor
│   │   │   ├── GridToolbar.razor
│   │   │   ├── GridTable.razor
│   │   │   ├── GridPagination.razor
│   │   │   ├── GridRow.razor
│   │   │   ├── GridColumnHeaders/
│   │   │   │   ├── SortableHeader.razor
│   │   │   │   └── StaticHeaderCell.razor
│   │   │   └── GridRowCells/
│   │   │       ├── ActionCell.razor
│   │   │       ├── LinkCell.razor
│   │   │       ├── BadgeCell.razor
│   │   │       ├── BadgeListCell.razor
│   │   │       ├── TextCell.razor
│   │   │       └── AvatarCell.razor
│   │   ├── Modals/
│   │   │   ├── FormModal.razor
│   │   │   └── ConfirmationModal.razor
│   │   ├── Toast/
│   │   │   └── Toast.razor
│   │   ├── Form/
│   │   │   └── FormMemberValidationError.razor
│   │   └── Icons/
│   │       └── (35 SVG icon components)
│   ├── Repositories/
│   │   └── Details/
│   └── PackageVulnerabilities/
│
├── Configuration/                      ◄── Startup configuration
│   ├── ConfigureApplication.cs
│   ├── ConfigureEndpoints.cs
│   └── UserIdAuditInterceptor.cs
│
├── Authentication/
│   └── AspNetSessionStateManager.cs
│
├── Enums/
│   ├── HxMethod.cs
│   ├── ModalConfirmationType.cs
│   └── BadgeType.cs
│
└── wwwroot/
    ├── htmx.min.js                     HTMX v2.0.4
    ├── site.js                         Sidebar toggle, toast dismiss, nav state
    ├── app.css                         Tailwind source
    └── styles.css                      Compiled Tailwind output
```

---

## What Is Built Well

### 1. The IEndpoint Static Abstract Pattern

**Location:** `PVT.Web/Application/IEndpoint.cs`

```csharp
public interface IEndpoint
{
    static abstract RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app);
}
```

This is an elegant use of C#'s static abstract interface members. Each endpoint class is self-contained: it declares its own route, HTTP method, authorization policy, and handler — all in one file. There's no separate controller class, no `[Route]` attributes scattered across a hierarchy, and no convention-based routing magic. Every endpoint is explicit about what it does and where it lives.

**Why it's good:**
- One file = one feature operation. `ClientCreateEndpoint.cs` contains the route, the GET handler (return empty form), and the POST handler (process submission). Nothing else.
- The `static abstract` constraint means you can't accidentally create an endpoint that forgets to register itself.
- The return type `RouteHandlerBuilder` enables chaining `.RequireAuthorization()`, `.AddEndpointFilter()`, etc.

---

### 2. Centralised Route Constants With Extension Methods

**Location:** `PVT.Web/Application/AppRoutes.cs`

```csharp
extension(string route)
{
    public string FromApi() => $"/api{route}";
    public string WithId(int id) => route.Replace("{id:int}", id.ToString());
    public string WithQueryParameters(string queryParameters) => $"{route}?{queryParameters}";
}
```

Every route in the application is a `const string` in `AppRoutes`. No magic strings in endpoints or Razor components. The extension methods transform routes fluently:

```csharp
// In an endpoint
app.MapPost(AppRoutes.ClientsCreate, Endpoint);

// In a Razor component
hx-get="@AppRoutes.ClientsSearch.FromApi()"

// Building a confirmation URL
ConfirmRoute = AppRoutes.ClientsDelete.WithId(id).FromApi()
```

**Why it's good:**
- Rename a route in one place, and every endpoint + every component updates.
- The `.FromApi()` convention cleanly separates user-facing URLs (`/clients`) from HTMX-targeted API routes (`/api/clients`).
- `.WithId()` is type-safe and eliminates manual string interpolation in Razor markup.

---

### 3. Custom IResult Types for HTMX

**Location:** `PVT.Web/Application/Results/`

The application defines four custom `IResult` implementations that translate server-side outcomes into HTMX response headers:

| Result Type | HTMX Header Set | Behaviour |
|---|---|---|
| `HtmxRedirectResult` | `HX-Redirect: /url` | HTMX performs full page navigation |
| `HtmxRefreshResult` | `HX-Refresh: true` | HTMX reloads the current page |
| `HtmxRetargetResult` | `HX-Retarget: #selector` + `HX-Reswap: strategy` | HTMX swaps the response into a different element than the original `hx-target` |
| `RazorComponentResult` | (none — standard HTML body) | HTMX swaps the rendered component into the default `hx-target` |

These are composed via `HttpResultExtensions`:

```csharp
// Redirect after successful form submission
IResultExtensions.HtmxRedirect(AppRoutes.ClientsSearch)

// Return a component with parameters
IResultExtensions.Component<ClientForm>(new { clientCreateCommand, result.ValidationErrors })

// Retarget error to the toast container
IResultExtensions.HtmxRetargetResult<Toast>("#toast-container",
    new { result.ErrorMessage, Type = NotificationType.Error }, "beforeend")
```

**Why it's good:**
- Server-side code controls client-side navigation without JavaScript.
- The `Results<T1, T2, T3>` union type means every endpoint's return signature documents all possible outcomes.
- The `HtmxRetargetResult` is particularly clever — it lets a form submission that fails redirect the error to a completely different DOM element (e.g. the toast container) while keeping the form content intact.

---

### 4. The Generic DataGrid Component System

**Location:** `PVT.Web/Components/Generic/DataGrid/`

The DataGrid is a composable system of 10+ components that work together to render a fully interactive table with search, sort, pagination, and CRUD actions — all powered by HTMX with zero JavaScript.

```
┌─────────────────────────────────────────────────────────────┐
│  DataGrid                                                    │
│  ┌────────────────────────────────────────────────────────┐ │
│  │  GridToolbar                                            │ │
│  │  [Search input]                          [Create btn]   │ │
│  │  hx-get, hx-trigger="keyup delay:300ms"                │ │
│  └────────────────────────────────────────────────────────┘ │
│  ┌────────────────────────────────────────────────────────┐ │
│  │  GridTable                                              │ │
│  │  ┌──────────────────────────────────────────────────┐  │ │
│  │  │  Columns (SortableHeader × N)                     │  │ │
│  │  │  hx-get, hx-vals='js:{sortDirection: "..."}'     │  │ │
│  │  └──────────────────────────────────────────────────┘  │ │
│  │  ┌──────────────────────────────────────────────────┐  │ │
│  │  │  Rows (GridRow × N)                               │  │ │
│  │  │  ┌────────┬────────┬──────────┬────────┐         │  │ │
│  │  │  │LinkCell│TextCell│ BadgeCell │ActionCl│         │  │ │
│  │  │  └────────┴────────┴──────────┴────────┘         │  │ │
│  │  └──────────────────────────────────────────────────┘  │ │
│  └────────────────────────────────────────────────────────┘ │
│  ┌────────────────────────────────────────────────────────┐ │
│  │  GridPagination                                         │ │
│  │  [<<] [<] [1] [2] [3] [>] [>>]    Rows per page: [10] │ │
│  │  hx-get, hx-include="searchTerm,sortBy,sortDirection"   │ │
│  └────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

**Why it's good:**
- **Composable:** A new search grid (e.g. for a future "Vulnerability Search" page) requires only wiring together existing components with different column definitions and cell types.
- **State preservation:** Every interactive element (`GridToolbar`, `SortableHeader`, `GridPagination`) uses `hx-include` to forward the current search term, sort column, sort direction, and page size to the server. The server returns a complete new grid — no client-side state management needed.
- **Type-safe cells:** Generic cell components like `LinkCell<TItem>`, `BadgeCell<TItem>`, and `BadgeListCell<TItem, TValue>` use `ValueSelector` expressions for compile-time safety:
  ```razor
  <LinkCell TItem="ClientSearchResponse"
            Item="@client"
            ValueSelector="@(c => c.Name)"
            Route="@AppRoutes.ClientsView.WithId(client.Id)" />
  ```
- **Conditional rendering:** Admin-only columns (Actions) are gated by `SessionStateManager.IsUserSessionAdministrator()` — the column header and cell don't render at all for non-admin users.

---

### 5. Out-of-Band Toast Notification System

**Location:** `PVT.Web/Components/Generic/Toast/Toast.razor`

The Toast component uses HTMX's out-of-band (OOB) swap to inject itself into a fixed-position container without disrupting the main content swap:

```html
<!-- Toast.razor wraps itself with OOB instruction -->
<div hx-swap-oob="beforeend:#toast-container">
    <div class="..." role="alert" aria-live="assertive" aria-atomic="true">
        <!-- Icon, message, close button, auto-dismiss progress bar -->
    </div>
</div>
```

```html
<!-- The target container lives in SidebarLayout.razor -->
<div id="toast-container" class="fixed bottom-10 left-1/2 z-50 flex flex-col gap-3"></div>
```

**Why it's good:**
- The same endpoint can return both main content AND a toast — the toast arrives as an OOB fragment piggybacked on the response.
- The auto-dismiss progress bar uses CSS animation (`animate-toast-progress`) with a configurable `--progress-duration` CSS variable — no JavaScript timers.
- Supports multiple notification types (Success, Error, Warning, Info) with distinct visual treatment.
- The `beforeend` swap strategy means multiple toasts stack rather than replace each other.
- Manual dismiss via `window.removeToast()` with a fade-out animation.

---

### 6. Confirmation Modal Pattern

**Location:** `PVT.Web/Components/Generic/Modals/ConfirmationModal.razor`

Delete operations use a two-step confirmation flow:

```
1. User clicks "Delete" button
   └─► hx-get="/api/admin/clients/delete/5"
       └─► Server returns ConfirmationModal component
           └─► HTMX swaps it into #hx-modal-container

2. User clicks "Confirm" in modal
   └─► hx-delete="/api/admin/clients/delete/5"
       hx-target="#client-5"
       hx-swap="delete"
       └─► Server returns Toast (success/error)
           └─► HTMX deletes the #client-5 row from the DOM
               Toast appears via OOB swap
```

The modal component is fully parameterised:

```csharp
IResultExtensions.Component<ConfirmationModal>(new
{
    Title = "Delete Client",
    Description = $"Are you sure you want to delete {result.Value.Name}?",
    BodyContent = "This action cannot be undone.",
    ConfirmRoute = AppRoutes.ClientsDelete.WithId(id).FromApi(),
    HxTarget = $"#client-{result.Value.Id}",
    HxSwap = "delete",
    Method = HxMethod.DELETE,
    ModalConfirmationType = ModalConfirmationType.Danger
});
```

**Why it's good:**
- The server controls the confirmation text, the target element, the swap strategy, and the HTTP method — all type-safe C# parameters.
- The `GetHxAttributes()` method dynamically selects `hx-post`, `hx-delete`, or `hx-get` based on the `HxMethod` enum.
- Backdrop click and close button both remove the modal via `this.closest('.fixed').remove()`.
- The `hx-on:htmx:after-request` attribute defaults to removing the modal after the confirm action completes.
- Visual treatment adapts to `ModalConfirmationType` (Danger = red gradient, Warning = amber, Primary = indigo).

---

### 7. ClientNavigationFilter for Browser History

**Location:** `PVT.Web/Application/Filters/ClientNavigationFilter.cs`

```csharp
public sealed class ClientNavigationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var requestUrl = $"{context.HttpContext.Request.Path.Value}{context.HttpContext.Request.QueryString}";

        if (requestUrl.StartsWith("/api"))
        {
            context.HttpContext.Response.Headers.Append("HX-Push-Url", requestUrl.Replace("/api", ""));
        }

        return await next(context);
    }
}
```

**Why it's good:**
- Solves the fundamental HTMX/SPA problem: AJAX requests don't update the browser URL. This filter transparently adds `HX-Push-Url` to responses.
- The `/api` prefix convention means internal API routes (`/api/clients?pageNumber=2`) map cleanly to user-facing URLs (`/clients?pageNumber=2`) without duplication.
- Applied selectively via `.AddEndpointFilter<ClientNavigationFilter>()` on search/list endpoints — form submissions and modals don't push URLs.
- Query string is preserved, so bookmarking `/clients?searchTerm=acme&pageNumber=2&sortBy=Name` works correctly.

---

### 8. Dual-Route Endpoints (GET Page + POST Action)

**Location:** Every Create/Edit endpoint

The pattern of mapping both GET and POST in a single endpoint class is used consistently:

```csharp
public sealed class ClientCreateEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(AppRoutes.ClientsCreate.FromApi(), GetPage)
            .RequireAuthorization(nameof(RoleName.Admin));

        return app.MapPost(AppRoutes.ClientsCreate, Endpoint)
            .RequireAuthorization(nameof(RoleName.Admin));
    }

    // GET: Return empty form
    private static RazorComponentResult GetPage()
        => IResultExtensions.Component<ClientForm>(new { clientCreateCommand = new ClientCreateCommand() });

    // POST: Process submission
    private static async Task<Results<HtmxRedirectResult, RazorComponentResult>> Endpoint(
        [FromForm] ClientCreateCommand clientCreateCommand,
        [FromServices] ClientCreateHandler clientCreateHandler)
    {
        var result = await clientCreateHandler.Handle(clientCreateCommand);
        return result switch
        {
            { IsSuccess: true } => IResultExtensions.HtmxRedirect(AppRoutes.ClientsSearch),
            { ValidationErrors: not null } => IResultExtensions.Component<ClientForm>(
                new { clientCreateCommand, result.ValidationErrors }),
            { IsSuccess: false } => IResultExtensions.Component<ClientForm>(
                new { clientCreateCommand, result.ErrorMessage })
        };
    }
}
```

**Why it's good:**
- GET and POST are co-located — you can see the full lifecycle of a form in one file.
- On validation failure, the POST returns the **same form component** with the original input preserved and errors attached. No redirect-on-error, no lost state.
- The `Results<HtmxRedirectResult, RazorComponentResult>` return type makes all outcomes explicit.

---

### 9. Fluent Endpoint Registration Chain

**Location:** `PVT.Web/Configuration/ConfigureEndpoints.cs`

```csharp
public static void MapApplicationRequestHandlers(this WebApplication app)
{
    var web = app.MapGroup("/");

    web.MapEndpoints<LoginEndpoint>()
        .MapEndpoints<LogoutEndpoint>()
        .MapEndpoints<HomePageEndpoint>()
        .MapEndpoints<ToggleAdminEndpoint>();

    web.MapEndpoints<ClientSearchEndpoint>()
        .MapEndpoints<ClientDetailsEndpoint>()
        .MapEndpoints<ClientCreateEndpoint>()
        .MapEndpoints<ClientEditEndpoint>()
        .MapEndpoints<ClientDeleteEndpoint>();
    // ...
}
```

**Why it's good:**
- One place to see every endpoint in the application, grouped by feature.
- Adding a new endpoint = one new line in the chain + one new class.
- The generic `MapEndpoints<T>()` helper calls `T.MapEndpoint(app)` via the static abstract interface — no reflection, no convention scanning.

---

### 10. PageLayout as the HTMX Orchestration Hub

**Location:** `PVT.Web/Components/Layout/PageLayout.razor`

```razor
<TopBarComponent TitleDefaults="@Title" DescriptionDefaults="@Description" />

<div id="hx-modal-container"></div>

<div class="p-4 sm:p-4 sm:pt-0" id="hx-page-container">
    @ChildContent
</div>

<div hx-get="@AppRoutes.Notifications.WithId(UserId).FromApi()"
     hx-trigger="every 15s"></div>
```

This single component defines the three critical HTMX swap targets:

| Target ID | Purpose | Swap Strategy |
|---|---|---|
| `#hx-page-container` | Main content area. Search grids, detail pages, and forms swap here. | `innerHTML` |
| `#hx-modal-container` | Modals inject here. Overlays the page content. | `innerHTML` |
| `#toast-container` | Toast notifications append here (in SidebarLayout). | `beforeend` (via OOB) |
| `#metadata-container` | Page title/description updates here (in TopBarComponent). | `innerHTML` (via OOB) |

**Why it's good:**
- Every HTMX interaction in the application targets one of these four well-known containers. There's no ad-hoc targeting of random DOM IDs scattered across components.
- The notification polling div (`hx-trigger="every 15s"`) is invisible — it fetches notifications and they arrive as OOB toast swaps without any visible trigger element.

---

## HTMX Patterns Catalogue

This section documents every distinct HTMX pattern used in the application, with the exact markup and the server-side response that pairs with it.

### Pattern 1: Full Page Content Swap

**Used by:** Sidebar navigation links

```html
<a hx-get="@item.HxRoute"
   hx-swap="innerHTML transition:true"
   hx-target="#hx-page-container"
   hx-push-url="@item.Route">
    @item.Label
</a>
```

**Server returns:** A full `RazorComponentResult` (e.g. `ClientsSearchTemplate`) that replaces the entire page container. The `transition:true` modifier enables CSS View Transitions for smooth page-to-page animation.

---

### Pattern 2: Debounced Search

**Used by:** `GridToolbar.razor`

```html
<input type="text"
       name="searchTerm"
       value="@SearchTerm"
       hx-get="@PageRoute.FromApi()"
       hx-trigger="keyup changed delay:300ms"
       hx-swap="innerHTML transition:true"
       hx-target="#hx-page-container"
       hx-include="[name='sortBy'], [name='sortDirection'], [name='pageSize']" />
```

**How it works:**
1. User types in the search box.
2. HTMX waits 300ms after the last keyup (`delay:300ms`).
3. Fires a GET request with the search term as a query parameter (via the input's `name` attribute).
4. Also includes the current sort and page size state (`hx-include`).
5. Server returns the entire grid component (toolbar + table + pagination) with filtered results.

**Why 300ms:** Long enough to avoid firing on every keystroke, short enough to feel responsive. The `changed` modifier ensures the request only fires if the value actually changed.

---

### Pattern 3: Stateful Pagination

**Used by:** `GridPagination.razor`

```html
<button name="pageNumber"
        value="@(page)"
        hx-get="@Route.FromApi()"
        hx-swap="innerHTML"
        hx-trigger="click"
        hx-target="#hx-page-container"
        hx-include="[name='searchTerm'], [name='sortBy'], [name='sortDirection'], [name='pageSize']">
    @page
</button>
```

**How state is preserved:** The `name="pageNumber" value="@page"` attribute on the button itself contributes the page number to the request. The `hx-include` gathers the search term, sort column, sort direction, and page size from other named elements on the page. The server receives all parameters and returns a new grid with the correct page of data.

The page size dropdown uses the same approach with `hx-vals="js:{pageNumber: 1}"` to reset to page 1 when the page size changes.

---

### Pattern 4: Column Sorting With State Preservation

**Used by:** `SortableHeader.razor`

```html
<button name="sortBy"
        value="@SortKey"
        hx-get="@Route.FromApi()"
        hx-swap="innerHTML transition:true"
        hx-target="#hx-page-container"
        hx-include="[name='searchTerm'], [name='pageSize']"
        hx-vals='js:{sortDirection: "@CurrentSortDirection", pageNumber: 1}'>
    @HeaderText
    @RenderSortIcon(SortKey)
</button>
```

**How direction toggling works:** The `CurrentSortDirection` property computes the opposite direction:

```csharp
private string CurrentSortDirection =>
    SortBy == SortKey && SortDirection == SortDirection.Ascending
        ? "Descending"
        : "Ascending";
```

The `hx-vals` attribute injects this computed value as a JavaScript object. Combined with the button's `name="sortBy"` and `value="@SortKey"`, the full query becomes something like `?sortBy=Name&sortDirection=Descending&pageNumber=1&searchTerm=acme&pageSize=10`.

The icon switches between ascending, descending, and default states based on the current sort state — all server-rendered, no JavaScript toggle logic.

---

### Pattern 5: Out-of-Band (OOB) Swap for Toasts

**Used by:** `Toast.razor`

```html
<div hx-swap-oob="beforeend:#toast-container">
    <div role="alert">
        <!-- Toast content, auto-dismiss progress bar -->
    </div>
</div>
```

**Server side:**
```csharp
// Endpoint returns a Toast component on error
return IResultExtensions.Component<Toast>(new {
    Message = "Successfully deleted client.",
    Type = NotificationType.Success
});
```

**How OOB works:** When HTMX processes a response, it finds any elements with `hx-swap-oob` and swaps them into the specified target *before* processing the main swap. This means a single response can update the main content area AND append a toast notification.

---

### Pattern 6: OOB Swap for Page Metadata

**Used by:** `TopBarMetaData.razor`

```html
<div hx-swap-oob="innerHTML:#metadata-container">
    <h1>Client Management</h1>
    <p>Search for clients...</p>
</div>
```

**How it works:** Each search grid component includes a `TopBarMetaData` element at the top. When HTMX swaps the grid into `#hx-page-container`, the `TopBarMetaData` OOB fragment simultaneously updates the page title in the top bar — a different DOM location entirely. This keeps the page header synchronized with the content without a separate AJAX call.

---

### Pattern 7: Modal Injection

**Used by:** Delete endpoints, manual scan endpoint

```
Step 1: User clicks delete icon
    hx-get="/api/admin/clients/delete/5"
    hx-target="#hx-modal-container"
    hx-swap="innerHTML"
        → Server returns ConfirmationModal component
        → HTMX injects modal into #hx-modal-container

Step 2: User clicks confirm button in modal
    hx-delete="/api/admin/clients/delete/5"
    hx-target="#client-5"
    hx-swap="delete"
    hx-on:htmx:after-request="this.closest('.fixed').remove()"
        → Server returns Toast component (OOB)
        → HTMX removes #client-5 from DOM
        → HTMX event handler removes the modal
        → Toast appears via OOB swap
```

---

### Pattern 8: Delete With Row Removal

**Used by:** `ConfirmationModal.razor`

```html
<button hx-delete="/api/admin/clients/delete/5"
        hx-target="#client-5"
        hx-swap="delete"
        hx-on:htmx:after-request="this.closest('.fixed').remove()">
    Delete
</button>
```

The `hx-swap="delete"` strategy removes the target element from the DOM entirely. Combined with `hx-target="#client-5"`, this removes the specific table row for the deleted client. The modal self-removes via the `after-request` event handler.

---

### Pattern 9: Retarget on Error

**Used by:** Edit endpoints, create endpoints

```csharp
// Server-side: redirect error to toast container
return IResultExtensions.HtmxRetargetResult<Toast>(
    "#toast-container",
    new { result.ErrorMessage, Type = NotificationType.Error },
    "beforeend");
```

The `HtmxRetargetResult` sets `HX-Retarget: #toast-container` and `HX-Reswap: beforeend` response headers. This tells HTMX to ignore the original `hx-target` and instead swap the response into the toast container. This is used when a GET request for an edit form fails (e.g. entity not found) — instead of replacing the page content with an error, the error appears as a toast while the current page remains intact.

---

### Pattern 10: Server-Side Redirect via Header

**Used by:** Login, form submissions

```csharp
// Server-side
return IResultExtensions.HtmxRedirect(AppRoutes.ClientsSearch);
// Sets header: HX-Redirect: /clients
```

HTMX intercepts the `HX-Redirect` header and performs a full page navigation. This is used after successful form submissions to navigate away from the form page. Unlike a standard HTTP 302, this works within HTMX's AJAX request lifecycle.

---

### Pattern 11: Polling for Notifications

**Used by:** `PageLayout.razor`

```html
<div hx-get="@AppRoutes.Notifications.WithId(UserId).FromApi()"
     hx-trigger="every 15s"></div>
```

An invisible div polls the notification endpoint every 15 seconds. The server returns a `NotificationToastList` component (which contains multiple `Toast` components with OOB swaps). If there are no notifications, the server returns `200 OK` with an empty body. The `Toast` components self-dismiss after their progress bar animation completes.

---

### Pattern 12: Browser History Management

**Used by:** `ClientNavigationFilter` + sidebar links

The dual-URL system works as follows:

1. Sidebar link: `hx-push-url="/clients"` — sets the browser URL directly.
2. Search/paginate actions: `ClientNavigationFilter` strips `/api` from the request path and sets `HX-Push-Url: /clients?pageNumber=2&searchTerm=acme`.
3. `site.js` listens for `htmx:afterSettle` and `popstate` events to update the sidebar's active state based on the current URL.

This means:
- Bookmarks work (URL contains full query state).
- Browser back/forward works (HTMX respects `popstate`).
- The sidebar highlights the correct section after every navigation.

---

### Pattern 13: Progressive Form Wizards

**Used by:** `RepositoryCreateEndpoint`

The repository creation flow uses a multi-step form:

```
Step 1: GET /api/admin/repositories/create/5
    → Returns RepositoryUrlForm (enter URL + select provider)

Step 2: POST /api/admin/repositories/validate/5
    → Server fetches repo metadata from GitHub/Bitbucket
    → Returns RepositoryForm (pre-filled with metadata)
    → OR returns RepositoryUrlForm with validation errors

Step 3: POST /api/admin/repositories/create/5
    → Creates the repository
    → Redirects to client details page
```

Each step replaces the previous form component in-place. The user never leaves the modal — the content evolves through the wizard. Validation errors at any step return the current step's form with errors displayed.

---

### Pattern 14: In-Place Badge Replacement

**Used by:** `RepositoryManualScanEndpoint`

```csharp
// After triggering a manual scan, return a "Scan Pending" badge
return IResultExtensions.Component<ManualScanPendingBadge>(new { });
```

The confirmation modal's `hx-target` points to `#manual-scan-{id}`, and the response replaces the scan button with an amber "Scan Pending" badge. This provides immediate visual feedback without a page refresh.

---

## Areas for Improvement

### 1. Repeated hx-include Attribute Strings

The string `[name='searchTerm'], [name='sortBy'], [name='sortDirection'], [name='pageSize']` appears identically in `GridToolbar`, `GridPagination`, and `SortableHeader`. If a new parameter is added to the query (e.g. `[name='filterBy']`), every component needs updating.

**Recommendation:** Define the include selector once as a constant and pass it as a parameter, or use a hidden input group that all grid components share by convention.

---

### 2. No Client-Side Validation

All validation happens server-side via FluentValidation. While this is correct (server validation is mandatory), the user experience involves a round-trip for every validation error. Basic constraints like "required field" or "minimum length" could be enforced client-side to provide instant feedback.

**Recommendation:** Add HTML5 validation attributes (`required`, `minlength`, `maxlength`, `pattern`) to form inputs. These work without JavaScript and don't replace server-side validation — they supplement it.

---

### 3. Inconsistent Error Response Targets

Some endpoints return errors as `RazorComponentResult` (replacing the current content), some use `HtmxRetargetResult` (targeting `#toast-container`), and some mix both in the same endpoint. The error display location depends on which endpoint you're in, not on the error type.

**Recommendation:** Establish a convention: validation errors re-render the form (keep user input), business errors toast, infrastructure errors toast. Apply this consistently.

---

### 4. Missing Return in RepositoryDetailsEndpoint

**Location:** `RepositoryDetailsEndpoint.cs:22`

```csharp
if (id is null)
{
    // Bug: creates a component but doesn't return it
    IResultExtensions.Component<Toast>(new { ... });
}
```

The null check creates a Toast component but doesn't `return` it. Execution falls through to the handler call with `id = null`.

---

### 5. Component Location Inconsistency

Some feature components live under `Application/Features/` (Clients, Users, Login) while others live under `Components/` (Repositories, PackageVulnerabilities). This split makes it harder to find components for a given feature.

**Recommendation:** Pick one convention. The `Application/Features/` approach (co-locating endpoint + component in the same feature folder) is stronger because it groups everything related to a feature together.

---

### 6. No Loading Indicators

HTMX requests that take time (e.g. repository validation which calls external APIs) have no visual loading state. The user gets no feedback between clicking a button and receiving the response.

**Recommendation:** Use HTMX's `hx-indicator` attribute or the `htmx:beforeRequest` / `htmx:afterRequest` events to show/hide a spinner. See the [Loading State Pattern](#loading-state-pattern) section below.

---

### 7. Inline onclick Handlers in Modals

The modal backdrop and close button use `onclick="this.closest('.fixed').remove()"`. While functional, inline handlers are a different paradigm from the data-attribute approach used elsewhere (e.g. `data-toggle="sidebar"`).

**Recommendation:** Use `hx-on:click` or data attributes consistently for DOM manipulation.

---

### 8. Tailwind Rebuilds on Every Debug Compile

```xml
<Target Name="TailwindOnBuild" BeforeTargets="Compile">
    <Exec Command="npx tailwindcss -i wwwroot/app.css -o wwwroot/styles.css --minify"
          Condition="'$(Configuration)' == 'Debug'" />
</Target>
```

This runs on every build, even when no `.razor` file changed. On larger projects this adds noticeable compile latency.

**Recommendation:** Use `npx tailwindcss --watch` in a separate terminal during development, and only run the minified build in Release/CI.

---

## Scaling These Patterns: A Repeatable HTMX Component Framework

The patterns above are solid but currently expressed ad-hoc in each feature. Below are ways to formalise them into conventions that make building new features faster and more consistent.

### Formalising the DataGrid as a Convention

The current DataGrid works well but requires manual wiring in each search grid component. A more convention-driven approach would reduce the per-feature code to just column definitions.

```razor
@* Proposed: A strongly-typed grid that only needs column config *@

<SearchGrid TItem="ClientSearchResponse"
            Route="@AppRoutes.ClientsSearch"
            CreateRoute="@AppRoutes.ClientsCreate"
            CreateButtonText="Create new client"
            Items="@Items"
            Pagination="@Pagination"
            SearchTerm="@SearchTerm"
            SortBy="@SortBy"
            SortDirection="@SortDirection">

    <ColumnDefinitions>
        <SortableColumn Property="c => c.Name" Width="15%" />
        <SortableColumn Property="c => c.Description" Width="20%"
                        Truncate="50" />
        <StaticColumn Header="Repositories" Width="20%">
            <BadgeListCell Items="@context.Repositories"
                           TextSelector="r => r.Name"
                           LinkSelector="r => AppRoutes.RepositoriesView.WithId(r.Id)" />
        </StaticColumn>
        <SortableColumn Property="c => c.IsActive" Width="13%"
                        RenderAs="ActiveBadge" />
        <AdminActionColumn Width="12%"
                           EditRoute="c => AppRoutes.ClientsEdit.WithId(c.Id)"
                           DeleteRoute="c => AppRoutes.ClientsDelete.WithId(c.Id)" />
    </ColumnDefinitions>
</SearchGrid>
```

This reduces each new search grid from ~130 lines to ~25 lines while preserving full customisation.

---

### A Generic CRUD Endpoint Base

The Create, Edit, and Delete endpoints follow identical patterns. A base class could eliminate the boilerplate:

```csharp
// A convention for Create endpoints
public abstract class CreateEndpoint<TCommand, TForm> : IEndpoint
    where TForm : ComponentBase
{
    protected abstract string Route { get; }
    protected abstract string RedirectRoute { get; }
    protected abstract string AuthPolicy { get; }

    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(Route.FromApi(), GetPage).RequireAuthorization(AuthPolicy);
        return app.MapPost(Route, HandlePost).RequireAuthorization(AuthPolicy);
    }

    private static RazorComponentResult GetPage()
        => IResultExtensions.Component<TForm>(new { Command = Activator.CreateInstance<TCommand>() });

    private static async Task<Results<HtmxRedirectResult, RazorComponentResult>> HandlePost(
        [FromForm] TCommand command,
        [FromServices] ICommandHandler<TCommand> handler)
    {
        var result = await handler.Handle(command);
        return result switch
        {
            { IsSuccess: true } => IResultExtensions.HtmxRedirect(RedirectRoute),
            { ValidationErrors: not null } => IResultExtensions.Component<TForm>(
                new { Command = command, result.ValidationErrors }),
            _ => IResultExtensions.Component<TForm>(
                new { Command = command, result.ErrorMessage })
        };
    }
}

// Concrete endpoint becomes minimal
public sealed class ClientCreateEndpoint : CreateEndpoint<ClientCreateCommand, ClientForm>
{
    protected override string Route => AppRoutes.ClientsCreate;
    protected override string RedirectRoute => AppRoutes.ClientsSearch;
    protected override string AuthPolicy => nameof(RoleName.Admin);
}
```

---

### Standardised Error Response Pipeline

Define a middleware or endpoint filter that handles Result-to-HTMX-response mapping consistently:

```csharp
public sealed class HtmxErrorFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);

        // If the result is already an IResult, let it through
        if (result is not Result appResult || appResult.IsSuccess)
            return result;

        // Validation errors: return as-is (form will handle them)
        if (appResult.ValidationErrors is { Count: > 0 })
            return result;

        // Business errors: retarget to toast
        return IResultExtensions.HtmxRetargetResult<Toast>(
            "#toast-container",
            new { appResult.ErrorMessage, Type = NotificationType.Error },
            "beforeend");
    }
}
```

---

### Loading State Pattern

Add a global loading indicator using HTMX's built-in `htmx-request` CSS class:

```css
/* In app.css */
.htmx-indicator {
    display: none;
}
.htmx-request .htmx-indicator {
    display: inline-flex;
}
.htmx-request .htmx-hide-on-request {
    opacity: 0.5;
    pointer-events: none;
}
```

```html
<!-- In a button that triggers an action -->
<button hx-post="/api/repositories/validate/5"
        hx-target="#hx-modal-container"
        class="htmx-hide-on-request">
    Validate
    <span class="htmx-indicator">
        <svg class="animate-spin h-4 w-4"><!-- spinner SVG --></svg>
    </span>
</button>
```

For page-level loading, add a top progress bar:

```html
<!-- In SidebarLayout.razor -->
<div id="page-loading"
     class="htmx-indicator fixed top-0 left-0 right-0 h-1 bg-blue-500 z-[100]
            animate-pulse"></div>
```

```html
<!-- On navigable elements -->
<a hx-get="@route.FromApi()"
   hx-indicator="#page-loading"
   hx-target="#hx-page-container">
```

---

### Optimistic UI Pattern

For actions where the outcome is near-certain (e.g. toggling a boolean), swap the UI immediately and revert on failure:

```html
<!-- Toggle active status optimistically -->
<button hx-post="/api/admin/clients/toggle-active/5"
        hx-swap="outerHTML"
        hx-target="closest .status-badge"
        hx-on:htmx:before-request="this.closest('.status-badge').classList.toggle('active')"
        hx-on:htmx:response-error="this.closest('.status-badge').classList.toggle('active')">
</button>
```

This provides instant feedback while the server processes the request. On error, the change is reverted.

---

### Infinite Scroll Pattern

For future list views where pagination isn't ideal (e.g. audit logs), use HTMX's `revealed` trigger:

```html
<!-- Load more rows when this sentinel element becomes visible -->
<tr id="scroll-sentinel"
    hx-get="/api/admin/audit?page=@(CurrentPage + 1)"
    hx-trigger="revealed"
    hx-swap="beforebegin"
    hx-target="this">
    <td colspan="4">
        <span class="htmx-indicator">Loading more...</span>
    </td>
</tr>
```

The server returns new `<tr>` elements that insert before the sentinel. The sentinel itself is replaced with a new sentinel pointing to the next page, creating an infinite chain.

---

### Server-Sent Events (SSE) for Real-Time Updates

The current 15-second notification polling works but is wasteful when there are no notifications. HTMX has native SSE support:

```html
<!-- Replace polling with SSE -->
<div hx-ext="sse"
     sse-connect="/api/notifications/stream/@UserId"
     sse-swap="notification">
</div>
```

```csharp
// Server-side SSE endpoint
app.MapGet("/api/notifications/stream/{userId}", async (
    int userId,
    HttpContext context,
    INotificationService notifications) =>
{
    context.Response.ContentType = "text/event-stream";

    await foreach (var notification in notifications.StreamForUser(userId, context.RequestAborted))
    {
        await context.Response.WriteAsync($"event: notification\ndata: ");
        // Write rendered Toast component HTML
        await context.Response.WriteAsync($"\n\n");
        await context.Response.Body.FlushAsync();
    }
});
```

This eliminates unnecessary polling requests and delivers notifications instantly.

---

### Confirmation Pattern as a Convention

Wrap the confirmation flow into a reusable helper so new delete operations require minimal code:

```csharp
// Proposed: A reusable confirmation endpoint builder
public static class ConfirmableEndpoint
{
    public static void MapDeleteWithConfirmation<THandler>(
        IEndpointRouteBuilder app,
        string route,
        string entityName,
        Func<int, string> descriptionBuilder,
        Func<int, string> targetSelector,
        string authPolicy = nameof(RoleName.Admin))
        where THandler : class
    {
        app.MapGet(route.FromApi(), async ([FromRoute] int id, [FromServices] /* loader */) =>
        {
            // Return ConfirmationModal with standard parameters
        }).RequireAuthorization(authPolicy);

        app.MapDelete(route.FromApi(), async ([FromRoute] int id, [FromServices] THandler handler) =>
        {
            // Call handler, return Toast
        }).RequireAuthorization(authPolicy);
    }
}

// Usage becomes a single call
ConfirmableEndpoint.MapDeleteWithConfirmation<ClientDeleteHandler>(
    app,
    AppRoutes.ClientsDelete,
    entityName: "Client",
    descriptionBuilder: id => $"Are you sure you want to delete this client?",
    targetSelector: id => $"#client-{id}");
```

---

### Form Wizard Pattern

Formalise the multi-step form pattern used in repository creation:

```csharp
// A convention for multi-step forms
public interface IFormWizardStep<TState>
{
    Task<Result<TState>> Process(TState state);
    RazorComponentResult RenderStep(TState state);
    RazorComponentResult RenderErrors(TState state, Result result);
}

// Each step is a class
public class RepositoryUrlStep : IFormWizardStep<RepositoryWizardState> { ... }
public class RepositoryMetadataStep : IFormWizardStep<RepositoryWizardState> { ... }
public class RepositoryConfirmStep : IFormWizardStep<RepositoryWizardState> { ... }

// The wizard endpoint orchestrates steps
app.MapPost("/api/repositories/wizard/{step}", async (
    [FromRoute] int step,
    [FromForm] RepositoryWizardState state,
    [FromServices] IEnumerable<IFormWizardStep<RepositoryWizardState>> steps) =>
{
    var currentStep = steps.ElementAt(step);
    var result = await currentStep.Process(state);

    if (!result.IsSuccess)
        return currentStep.RenderErrors(state, result);

    var nextStep = step + 1 < steps.Count() ? steps.ElementAt(step + 1) : null;
    return nextStep?.RenderStep(result.Value)
        ?? IResultExtensions.HtmxRedirect(AppRoutes.ClientsView.WithId(state.ClientId));
});
```

---

## Summary

| Dimension | Assessment |
|---|---|
| **Endpoint architecture** | Excellent. `IEndpoint` static abstract is a clean, discoverable pattern. |
| **HTMX integration** | Very strong. Custom `IResult` types for HTMX headers are well-designed. |
| **Component reusability** | Strong. DataGrid, Toast, Modal are genuinely reusable. |
| **State management** | Clever. Server-side state via `hx-include` eliminates client-side stores. |
| **Browser history** | Well-handled. `ClientNavigationFilter` + `HX-Push-Url` solves the AJAX history problem. |
| **Error handling UX** | Good but inconsistent. Some errors toast, some re-render, no universal convention. |
| **Loading states** | Missing. No visual feedback during HTMX requests. |
| **Client-side validation** | Missing. All validation requires a server round-trip. |
| **Component organisation** | Slightly inconsistent between `Application/Features/` and `Components/`. |
| **Scalability of patterns** | High potential. Current patterns could be formalised into conventions that make new features near-mechanical to build. |

The foundation is strong. The main opportunity is to formalise what's already working into explicit conventions — generic CRUD endpoints, standardised error handling, loading indicators, and a SearchGrid convention — so that the next 10 features are built in a fraction of the time the first 5 took.
