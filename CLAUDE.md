# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository state

M0–M5 are all functionally done end to end (M4 — a second, structurally different workflow proving the model needs no new code, just using `/admin/workflows` — is a usage exercise, not a coding milestone, so there's nothing to build for it). Station runtime (M2): `/station/{id}` — claim, advance (button + Enter key), hand-off, and live cross-browser updates all verified working with Playwright. Workflow builder (M3): `/admin/workflows` (list, create, duplicate, archive/activate) and `/admin/workflows/{id}` (add/edit/delete/reorder stages and steps, upload/delete step media) — also verified working with Playwright, including the FK-Restrict delete guards. Orders (FR-6/FR-8): `/admin/orders` (create a WorkItem against any active workflow with stages, see every WorkItem's current stage/status/claimed station) — verified the FIFO claim order is respected. Timing review (M5, FR-17/FR-18): `/admin/timing` — per-completed-order, per-stage dwell-time breakdown, grouped by workflow — verified by driving one WorkItem through all 4 stages with Playwright and checking the displayed durations sum to the displayed total. Station setup (PRD §11.3): `/admin/stations` — create/rename/delete the physical stations that man a workflow's stages (the runtime only ever *reads* stations; this is where they're authored), with a delete guard that refuses to remove a station currently holding a claimed WorkItem. `FlowLine.Domain` has all seven entities with relationships, delete behaviors, and the optimistic concurrency token configured and migrated; the app auto-migrates and seeds a "Gaming PC Build" workflow (4 stages, 13 steps, 4 stations, 3 demo WorkItems) on startup. 42 passing tests total. Not yet done: CSV/bulk WorkItem import (FR-7, explicitly optional per the PRD) — everything else in the PRD's functional requirements is built. Follow the stack and milestones below rather than improvising a different architecture; they reflect deliberate decisions already made in the PRD, not just suggestions.

## Solution layout

- `src/FlowLine.Web` — Blazor Server app. Targets .NET 8, interactive server render mode. `Program.cs` runs `Database.Migrate()` + `SeedData.SeedAsync()` on startup — the app is self-setting-up, no manual `dotnet ef database update` needed in production (NFR-1). `IRelayNotifier` is `AddSingleton` (must outlive any one circuit); `IRelayService`/`IWorkflowBuilderService`/`IOrderService`/`ITimingService`/`IStationService` are `AddScoped` (match the DbContext's scope, one per Blazor circuit). Pages: `Home.razor` (`/`, station picker), `StationRun.razor` (`/station/{StationId:int}`, the station screen), `Admin/WorkflowList.razor` (`/admin/workflows`), `Admin/WorkflowEdit.razor` (`/admin/workflows/{WorkflowId:int}`), `Admin/Orders.razor` (`/admin/orders`), `Admin/TimingReview.razor` (`/admin/timing`), `Admin/StationManagement.razor` (`/admin/stations`) — all `@rendermode InteractiveServer`. Razor components talk only to the Application-layer services, never to `FlowLineDbContext` directly, to keep DB access funneled through one layer (NFR-7).
- `src/FlowLine.Domain/Entities` — the seven entities plus `WorkItemStatus` and `IConcurrencyAware`. Add new domain types here, not in Infrastructure or Web.
- `src/FlowLine.Infrastructure` — EF Core `FlowLineDbContext` (`Data/FlowLineDbContext.cs`: DbSets, relationship/delete-behavior config in `OnModelCreating`, concurrency-token bump in `SaveChanges`), migrations (`Data/Migrations/`), `Data/SeedData.cs` (idempotent — no-ops if any `Workflow` row exists), SQLite provider. References Domain.
- `src/FlowLine.Application/Relay` — the relay. `IRelayService`/`RelayService`: `ClaimNextAsync` (atomic claim with retry-on-conflict), `AdvanceAsync` (records a `StepCompletion`; hands off transactionally on a stage's last step), `GetQueueDepthAsync`, `GetStationAsync`/`GetStationsAsync`/`GetActiveWorkItemAsync` (read-only support for the UI). `IRelayNotifier`/`RelayNotifier`: a singleton in-process `event Action<int> StageChanged` that `RelayService` raises after a successful claim or hand-off — this *is* FlowLine's "live update" mechanism (see below).
- `src/FlowLine.Application/Builder` — the workflow builder (PRD §7.1, FR-1–FR-5). `IWorkflowBuilderService`/`WorkflowBuilderService`: full CRUD + reorder over Workflow/Stage/Step/MediaAsset, `DuplicateWorkflowAsync` (deep clone including copying — not sharing — underlying media files, so deleting one copy's media never affects the other), `AddMediaAssetAsync`/`DeleteMediaAssetAsync` (writes/deletes files under `MediaStorageOptions.RootPath`, which `Program.cs` points at `wwwroot/media`). Delete operations on Stage/Step catch `DbUpdateException` from the FK Restrict guards (see Domain model below) and rethrow as `WorkflowBuilderException` with a friendly message — never let a delete silently destroy WorkItem/timing history.
- `src/FlowLine.Application/Orders` — admin order management (PRD §7.2, FR-6/FR-8). `IOrderService`/`OrderService`: `CreateWorkItemAsync` (queues a new WorkItem at the chosen workflow's *first* stage by `OrderIndex` — throws `OrderServiceException` if the workflow has no stages), `GetWorkItemsAsync` (every WorkItem with Workflow/CurrentStage/ClaimedByStation loaded, newest first). Deliberately separate from `Relay` (worker-facing runtime, PRD §7.3) even though both touch `WorkItem` — `OrderService` only ever creates a WorkItem in the Queued state at stage 1; it never claims, advances, or hands one off, that's `RelayService`'s job alone.
- `src/FlowLine.Application/Timing` — timing review (PRD §7.4, FR-17/FR-18, M5). `ITimingService`/`TimingService`: `GetCompletedOrderTimingsAsync` returns every Completed WorkItem with a per-stage `TimeSpan` breakdown computed purely from `StepCompletion.CompletedAtUtc` timestamps plus `WorkItem.CreatedAtUtc` — see "Why timing needs CreatedAtUtc" below for why `QueuedAtUtc` alone can't do this.
- `src/FlowLine.Application/Stations` — admin station setup (PRD §11.3). `IStationService`/`StationService`: `GetStationsAsync` (every Station with its Stage and that Stage's Workflow loaded, ordered workflow → stage OrderIndex → name), `CreateStationAsync` (binds a station to a stage; throws `StationServiceException` if the stage doesn't exist), `UpdateStationAsync` (rename), `DeleteStationAsync` (catches `DbUpdateException` from the FK Restrict guard and rethrows as `StationServiceException` — a station holding a claimed WorkItem can't be deleted). Deliberately separate from `Relay`, which only ever *reads* stations (station picker + runtime) and never creates/edits/deletes them — mirrors the `OrderService`/`RelayService` split.
- `Relay`, `Builder`, `Orders`, `Timing`, and `Stations` are all business logic, not Infrastructure or Web (NFR-7) — all reference Domain + Infrastructure only.
- `tests/FlowLine.Tests` — xUnit. `Data/SqliteTestDatabase.cs` is the shared in-memory-SQLite helper. `Data/FlowLineDbContextTests.cs` covers the entity graph and the raw concurrency-token mechanism. `Relay/RelayServiceTests.cs` covers the relay end to end, including `ClaimNextAsync_TwoStationsRaceForOneItem_ExactlyOneWins` (genuine concurrent claim attempts via a SQLite shared-cache in-memory DB, not a sequential simulation) and two tests asserting `IRelayNotifier.StageChanged` fires (and doesn't fire) at the right times. `Builder/WorkflowBuilderServiceTests.cs` covers CRUD/reorder/duplicate and both delete-guard exceptions — note its delete-guard tests deliberately seed the blocking WorkItem/StepCompletion through a *second*, separate `FlowLineDbContext` (see "EF Core in-memory tracking can mask the FK guard it means to test" below). `Orders/OrderServiceTests.cs` covers first-stage queuing, the no-stages guard, and ordering. `Timing/TimingServiceTests.cs` covers the per-stage duration math directly (asserts exact `TimeSpan` values from known timestamps), exclusion of non-Completed WorkItems, and ordering. `Stations/StationServiceTests.cs` covers create/rename/delete, the blank-name guard (create and rename both reject empty/whitespace and trim), and the delete guard — its guard test seeds the blocking claimed WorkItem through a *second*, separate `FlowLineDbContext` for the same reason the builder guard tests do (see "EF Core in-memory tracking can mask the FK guard it means to test").

The seeded "Gaming PC Build" workflow content (stage/step names and instructions) is a plausible reconstruction, not a literal copy of the legacy Apps Script tool's text — no source for the original wording exists in this repo. Treat it as a structural fixture for proving the model (M1) and exercising the UI, not as content to preserve verbatim; it can be edited freely, including by the future workflow builder UI itself.

### Live updates without a hand-written SignalR Hub

PRD FR-16/NFR-9 ask for a hand-off on one station's screen to appear on another station's screen, on a different PC, without a manual refresh. This does **not** need a custom SignalR Hub: Blazor Server already holds one persistent SignalR circuit per browser tab. `IRelayNotifier` (singleton, `Application/Relay`) is a plain C# `event Action<int> StageChanged`; `RelayService` raises it with the affected stage's ID after a successful claim or hand-off. `StationRun.razor` subscribes in `OnInitializedAsync`, and on a matching event calls `InvokeAsync(() => { refresh; StateHasChanged(); })` — `InvokeAsync` is required because the event fires from whatever circuit's context triggered the change, not the subscribing component's own circuit. Unsubscribe in `DisposeAsync`, or you leak a delegate reference to a dead component on a singleton that outlives every circuit. Verified working with two real, separately-attached Playwright browser tabs (no shared navigation): advancing Station 1 through a hand-off updates Station 2's already-open tab with no `page.goto`/reload involved.

### A SQLite WAL gotcha when resetting the local dev database

If you delete `App_Data/flowline.db` to force a clean reseed, also delete `flowline.db-shm` and `flowline.db-wal` (WAL-mode sidecar files). Deleting only the main file and leaving a stale `-wal` behind causes SQLite to replay the *old* WAL into the new file on next open, silently reintroducing old seed data (hit this once — looked like new seed code wasn't running, when actually it ran fine but old data came back from the leftover WAL). `rm -f App_Data/flowline.db*` gets all three.

### Concurrency token note

SQLite has no native rowversion/timestamp type, so `WorkItem.RowVersion` (`Guid`) is an *application-managed* concurrency token: `FlowLineDbContext.SaveChanges`/`SaveChangesAsync` bump it to a new `Guid` for every modified `IConcurrencyAware` entity before calling `base.SaveChanges`, and it's marked `.IsConcurrencyToken()` in `OnModelCreating`. This is the standard EF Core pattern for providers without native rowversion support — don't replace it with a `[Timestamp]`/`IsRowVersion()` attribute, that only works on SQL Server. `RelayService.ClaimNextAsync` catches `DbUpdateConcurrencyException` and retries against the next-oldest queued WorkItem (PRD §6.5); `AdvanceAsync` deliberately does *not* retry on conflict — a concurrent advance on the same already-claimed WorkItem indicates a genuine double-call (e.g. a debounce failure), and the exception should propagate so the caller re-reads current state rather than silently double-processing a step.

### DateTime, not DateTimeOffset, for all timestamps

`WorkItem.QueuedAtUtc`/`CreatedAtUtc` and `StepCompletion.CompletedAtUtc` are `DateTime` (always UTC by convention), not `DateTimeOffset`. This isn't a style choice: the SQLite EF Core provider throws `NotSupportedException` translating `ORDER BY` over a `DateTimeOffset` column, and `ClaimNextAsync`'s "oldest queued" query and `TimingService`'s ordering both need to order by timestamp. Hit this once already (see migration `ChangeTimestampsToDateTime`) — don't reintroduce `DateTimeOffset` on a column you intend to filter/sort by.

### Why timing needs `CreatedAtUtc`, not just `QueuedAtUtc`

`WorkItem.QueuedAtUtc` is overwritten on every hand-off (it means "queued at the *current* stage," for `ClaimNextAsync`'s FIFO ordering — see the entity's XML doc). By the time a WorkItem reaches Completed, all of its earlier per-stage queued-at values are gone; only the most recent one survives. That makes it useless for reconstructing stage 1's start time after the fact, which is why `CreatedAtUtc` exists as a separate, *never-updated* field set once at creation. `TimingService` computes stage N's dwell time as `(last StepCompletion in stage N) − (last StepCompletion in stage N-1)`, using `CreatedAtUtc` as the stand-in end-of-"stage 0" for stage 1 — this way stage 1 isn't a special case, and the per-stage durations sum exactly to `CompletedAtUtc − CreatedAtUtc`. If you add a new WorkItem-creation path anywhere (besides `OrderService.CreateWorkItemAsync` and `SeedData`), don't forget `CreatedAtUtc` — it defaults to `DateTime.UtcNow` on the entity, so it's only a problem if you ever explicitly overwrite it after construction.

### `@bind` on a `Dictionary<TKey,TValue>` indexer needs the key pre-seeded

`@bind="someDictionary[key]"` is valid (the indexer has both a getter and setter, so it's an assignable target) but Blazor's generated code calls the getter on every render — if the key isn't present yet, that's an unhandled `KeyNotFoundException` that takes down the whole page (`WorkflowList.razor`'s per-row "duplicate as" field and `WorkflowEdit.razor`'s per-stage "new step name" field both do this). Fix is to seed every key with `TryAdd(key, string.Empty)` when the parent collection loads, before any indexer-bound input renders. Hit this in both places building the workflow builder UI — if you add another per-row/per-item text input bound to a dictionary, seed it the same way.

### `@inject` can't reuse the component's own class name

A Razor component's generated class is named after its file (`Orders.razor` → class `Orders`). `@inject IOrderService Orders` inside that file fails to compile with `CS0542: 'Orders': member names cannot be the same as their enclosing type` — the injected property collides with the class itself. Name the property something else (`OrderService` worked fine) rather than mirroring the service's domain name; this only bites when the component file and the injected interface happen to share a name; it didn't apply to `IRelayService`/`IWorkflowBuilderService` since none of the page filenames matched.

### EF Core in-memory tracking can mask the FK guard it means to test

When testing that a delete is correctly *blocked* by a Restrict FK (e.g. deleting a Stage that still has a WorkItem on it), don't seed the blocking row through the *same* `FlowLineDbContext` instance the method under test will use. EF Core auto-fixes-up navigation properties between any two tracked entities sharing a FK, regardless of which `Include` path loaded them — so the blocking row ends up attached to the in-memory object graph, and removing the parent throws `InvalidOperationException: ... has been severed ...` from EF's own change tracker *before* the call ever reaches the database. That's not the same failure as the real one (`DbUpdateException` from SQLite's FK constraint), and `WorkflowBuilderService`'s catch block won't catch it, so the test fails even though production code is correct — a fresh request/circuit in production always gets its own DbContext with no such stale tracking. Use a second, short-lived `FlowLineDbContext` to seed the conflicting row (see `WorkflowBuilderServiceTests`'s `DeleteStageAsync`/`DeleteStepAsync` guard tests), mirroring how two separate requests would actually behave.

## Commands

```bash
dotnet build                                    # build the whole solution
dotnet run --project src/FlowLine.Web           # run the app (binds 0.0.0.0:5000 per appsettings.json Kestrel config)
dotnet test                                     # run all tests
dotnet test --filter FullyQualifiedName~Foo     # run a single test/class by name

# EF Core migrations (run from repo root; dotnet-ef is a local tool, see .config/dotnet-tools.json)
dotnet tool restore                             # first time only, installs dotnet-ef
dotnet tool run dotnet-ef migrations add <Name> \
  --project src/FlowLine.Infrastructure/FlowLine.Infrastructure.csproj \
  --startup-project src/FlowLine.Web/FlowLine.Web.csproj \
  --output-dir Data/Migrations
dotnet tool run dotnet-ef database update \
  --project src/FlowLine.Infrastructure/FlowLine.Infrastructure.csproj \
  --startup-project src/FlowLine.Web/FlowLine.Web.csproj
```

The SQLite file lives at `src/FlowLine.Web/App_Data/flowline.db` (gitignored — it's runtime data, not source) and is created/updated by `dotnet ef database update`. The connection string is in `src/FlowLine.Web/appsettings.json`.

Kestrel is explicitly configured to bind `0.0.0.0:5000` (not the ASP.NET Core default `localhost`) and HTTPS redirection/HSTS were deliberately left out — per PRD §9.1 this is a plain-HTTP LAN app (`http://flowline:5000`), not an internet-facing service, so forcing TLS would just produce cert warnings on every station browser for no benefit. If a real deployment needs encryption, that's a deliberate config change, not a default to restore blindly.

## What FlowLine is

A self-hosted LAN web app that runs guided, multi-station assembly/teardown lines (replacing a legacy Google Apps Script + Google Sheet tool). The key design upgrade: **process definitions are data, not code** — an admin defines workflows (stages, steps, instructions, images) entirely through the UI (`/admin/workflows`), with no redeployment.

## Target tech stack (per PRD §9)

- **ASP.NET Core 8 + Blazor Server** (C#) — UI and logic live together server-side; each browser keeps a live SignalR connection. This is *not* a Blazor WebAssembly or separate JS-frontend app — that tradeoff was explicitly considered and rejected.
- **EF Core** as the data access layer — must abstract the DB provider so SQLite can later be swapped for the company database via config/connection-string change, not a rewrite (NFR-2, NFR-3).
- **SQLite (WAL mode)** for the prototype — single file on the server.
- **Local filesystem** folder for uploaded media (`MediaAsset` rows store paths, not blobs).

## Deployment model — read this before designing anything network-related

FlowLine is **one server process** on a LAN machine; every station and the admin UI are just browser clients pointing at it (e.g. `http://flowline:5000`). There is no per-station install and no file syncing between machines. A change at one station (e.g. a hand-off) must propagate to other stations' screens in real time via SignalR, not polling. Keep this client-server topology in mind — don't design features that assume a single local user/browser.

## Domain model (PRD §6) — the core abstraction to preserve

Two sides, deliberately separated:

**Template side** (authored by admin, defines a process):
```
Workflow 1───* Stage 1───* Step 1───* MediaAsset
Workflow 1───* Stage 1───* Station   (a station mans one stage)
```
- `Workflow` — a named process (e.g. "Gaming PC Build").
- `Stage` — an ordered station-position within a workflow (old "Step 1–4").
- `Step` — an ordered sub-task within a stage, with instructions (old "1.1 Pick Case").
- `MediaAsset` — image/GIF attached to a `Step`.

**Instance side** (runtime, the orders flowing through):
```
Workflow 1───* WorkItem (CurrentStage, Status, ClaimedByStation)
WorkItem 1───* StepCompletion *───1 Step
```
- `Station` — a physical screen bound to one `Stage` of one `Workflow`.
- `WorkItem` — a unit in flight (Order #, SKU, Qty, Channel), tracking `CurrentStageId`, `Status` (Queued / In Progress / Completed), `ClaimedByStationId`, and a row-version concurrency token.
- `StepCompletion` — one row per completed sub-step per WorkItem (step, station/operator, timestamp). This is the source of truth for cycle-time data — never go back to "timestamp in a positional column."

There is **no separate queue table**: a stage's queue is just "WorkItems with `CurrentStageId` = this stage and `Status` = Queued."

## The relay model — the central correctness requirement (NFR-4)

This is the most important behavior to get right; read PRD §6.5 before touching claim/hand-off logic. Implemented in `RelayService` (`src/FlowLine.Application/Relay`):

1. **Claim** (`ClaimNextAsync`): a free station claims the oldest Queued WorkItem at its stage (ordered by `QueuedAtUtc`, not `Id` — see the entities' XML docs) via an atomic, guarded conditional update (`WHERE Status = Queued AND ClaimedByStationId IS NULL`) plus the optimistic concurrency token. Exactly one station wins if two race; the loser's `SaveChanges` throws `DbUpdateConcurrencyException`, which the method catches and retries against the next-oldest candidate. Two stations on the same stage can never claim the same unit.
2. **Work** (`AdvanceAsync`): determines the WorkItem's next outstanding step itself (from existing `StepCompletions` at the current stage) rather than trusting a caller-supplied step ID, and writes a `StepCompletion` with a server timestamp.
3. **Hand-off:** completing a stage's last step is **one transaction** that sets `CurrentStageId` to the next stage, resets `Status` to Queued, refreshes `QueuedAtUtc`, and clears the claim — or marks the WorkItem Completed if there's no next stage.
4. **Buffering:** stages naturally buffer WorkItems when downstream is slower; nothing is lost or overwritten.
5. **Live updates:** when a WorkItem lands in a stage's queue, that stage's station screen(s) update with no manual refresh, via `IRelayNotifier` (see "Live updates without a hand-written SignalR Hub" below) — verified working across two separate browser tabs.

A claim or hand-off must never be a non-atomic read-then-write — that's exactly the bug class (row deletes, no locking) the legacy system had and FlowLine is meant to fix.

## Scope discipline

The PRD explicitly excludes (§4) for the prototype: company DB/ERP integration, real auth/roles (admin/worker distinction may be stubbed), multi-site/cloud/HA, native mobile apps, and analytics dashboards beyond raw timing + a basic view. Don't build toward these unless asked — the milestone sequence (§10) targets M4 (a second, structurally different workflow running with no code changes) as the definition of prototype success.

Several questions are explicitly open and **assumed resolved one way** for the prototype unless told otherwise (§11): workflows are linear (no branching), work only moves forward (no rework/send-back), and one station per stage is the default (multi-station-per-stage is allowed by the data model but not necessarily exposed in the UI).
