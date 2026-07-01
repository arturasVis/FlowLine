# Product Requirements Document — Workflow Line System (working title: "FlowLine")

**Status:** Draft v0.1 (prototype scope)
**Author:** _[you]_
**Last updated:** 30 June 2026

---

## 1. Summary

FlowLine is a self-hosted web application for running and timing multi-station assembly/teardown lines. It replaces an existing Google Apps Script tool that guided workers through a fixed 4-step gaming-PC build.

The core upgrade over the old system: **process definitions are data, not code.** A non-developer can define a new workflow (e.g. "Office PC Build", "Laptop Disassembly", "RMA Teardown"), give it any number of stages and sub-steps with instructions and images, and run orders through it — without touching the application or redeploying anything.

This document covers the **prototype**. It is intentionally scoped to prove the model on a single machine with a local database before any integration with company systems.

---

## 2. Background & motivation

The legacy system (Google Apps Script + a single Google Sheet) worked but had structural problems:

- Each of the 4 build steps was a **separate hand-coded HTML file** with hardcoded step names, image URLs, and page links. Adding or changing a step meant editing HTML.
- The process was **hardwired** — it only ever modelled one gaming-PC build. A different process (disassembly, laptops, office builds) would require duplicating and rewriting everything.
- Orders flowed by **physically moving rows between sheet tabs** (`Orders → Step1 → Step2 …`) with row deletes and no locking, which is fragile under concurrent stations.
- Logic was split awkwardly between brittle client-side JavaScript and server functions, with a half-finished refactor left in place.

FlowLine keeps the good ideas (station screens, step-by-step guidance, per-step timing, orders advancing down a line) and rebuilds them on a proper data model.

---

## 3. Goals

| # | Goal |
|---|------|
| G1 | Run guided, multi-stage workflows on per-station screens, advanced by the worker. |
| G2 | Let an admin define and edit workflows (stages, sub-steps, instructions, images) through the UI — no code changes. |
| G3 | Support multiple distinct process types simultaneously (build, disassembly, laptop, office PC, etc.). |
| G4 | Record a timestamp per sub-step so cycle times can be reviewed per stage and per order. |
| G5 | Be fully self-contained: one server application with one local database file and local media, hosted on a single LAN machine that all station PCs connect to as browser clients — no cloud or external services. |
| G6 | Be built in C# as a web app, structured so the database can later be swapped for the company system with minimal change. |

## 4. Non-goals (for the prototype)

- Integration with the company database, ERP, or order source. (Orders will be entered/seeded manually.)
- Authentication, roles, and permissions beyond a trivial admin/worker distinction (may be stubbed or omitted).
- Multi-site / cloud deployment, high-availability, or horizontal scaling.
- Mobile-native apps. (Responsive web is enough; screens are stationary monitors.)
- Reporting dashboards and analytics beyond raw timing capture and a basic view.

These are explicitly deferred so the prototype stays small and the core model gets validated first.

---

## 5. Users & personas

- **Line worker / operator.** Stands at **one station, which owns one stage** of a workflow. Sees the unit currently at their station and the current sub-step with its instructions/images. Presses a key or button to advance, and when they finish their stage the unit is handed to the next worker down the line while they pull the next unit. Multiple workers run the same workflow at the same time, one per stage. Wants zero friction and large, glanceable visuals.
- **Line admin / process engineer.** Defines workflows, edits steps, reorders them, uploads images, and seeds orders. Reviews timing. Comfortable with the app but **not a programmer**.
- **(Later) Manager.** Wants throughput and cycle-time reporting. Out of scope for the prototype beyond raw data being available.

---

## 6. Core concept & domain model

The single most important design decision: separate the **template** (how a process is defined) from the **instance** (a specific order moving through it).

### 6.1 Template side (definitions — authored by admin)

- **Workflow** — a named process. e.g. "Gaming PC Build", "Laptop Disassembly". Has a name, description, active flag.
- **Stage** — an ordered station within a workflow (the old "Step 1–4"). Has a name, an order index, and belongs to a Workflow.
- **Step** (sub-step) — an ordered task within a stage (the old "1.1 Pick Case", "1.2 Remove Panels"). Has a name, instruction text, an order index, and belongs to a Stage.
- **MediaAsset** — an image or GIF attached to a Step. Stored as a file path/reference plus display order.

### 6.2 Instance side (runtime — the orders flowing through)

- **Station** — a physical screen/position bound to one Stage of one Workflow (e.g. "Build Line A — Stage 2"). This is how the app knows which stage a given monitor is running. A worker sits at a station for their shift. Normally one station per stage; more than one station may share a stage if extra parallel capacity is wanted (see §6.5).
- **WorkItem** — the thing being processed. Carries the business fields from the old sheet: Order #, SKU, Qty, Channel. Bound to one Workflow. Tracks **which stage it is currently in** (`CurrentStageId`) and a **status** (Queued at a stage / In Progress at a stage / Completed). To make hand-off concurrency-safe it also carries a **claim** (`ClaimedByStationId`, nullable) and an optimistic concurrency token (row version).
- **StepCompletion** — one row per sub-step completed for a WorkItem: which step, which station/operator (optional), and the timestamp. This replaces the "write a time into the next empty column" trick and gives clean, queryable cycle-time data.

### 6.3 Why this matters

- Adding a step = inserting a `Step` row. Reordering = changing index values. New process = a new `Workflow` with its own stages/steps. **None of this touches code.**
- Timing becomes real relational data (`StepCompletion`), not positional columns in a sheet — so "average time for stage 2 on laptop teardowns last week" is a simple query.
- A WorkItem advancing a stage is a status/foreign-key update inside a transaction — no row-moving or deleting, no concurrency stomping.

### 6.4 Relationships (text form)

```
Workflow 1───* Stage 1───* Step 1───* MediaAsset
Workflow 1───* Stage 1───* Station            (a station mans one stage)
Workflow 1───* WorkItem  (CurrentStage, Status, ClaimedByStation)
WorkItem 1───* StepCompletion *───1 Step
```

### 6.5 The relay: how work passes between workers

This is the core operating model. The workflow runs as a pipeline of staffed stations, with one or more units in flight at once.

- **Per-stage queue.** A stage's inbound queue is simply "the WorkItems whose `CurrentStageId` is this stage and whose status is Queued." There is no separate queue table — it falls out of the WorkItem's own fields.
- **Claiming a unit.** When a station is free, it claims the oldest Queued unit at its stage. The claim is a single atomic update guarded by `WHERE Status = Queued AND ClaimedByStationId IS NULL`, protected by the row-version token. If two stations on the same stage try at once, exactly one wins; the other simply retries and gets the next unit. This is what the old row-delete approach could never do safely.
- **Working it.** As the worker advances through that stage's sub-steps, each advance writes a `StepCompletion` timestamp.
- **Hand-off.** When the last sub-step of the stage is completed, a single transaction sets the unit's `CurrentStageId` to the next stage, status back to Queued, and clears the claim. The unit is now sitting in the next stage's queue. The upstream station immediately claims its next unit. If there is no next stage, the unit is marked Completed instead.
- **Buffering.** Because hand-off just changes which stage a unit is queued at, a fast upstream worker can build up a backlog in front of a slower downstream worker without anything being lost or overwritten — the line self-buffers.
- **Live screens.** When a unit lands in a stage's queue, that station's screen updates in real time (Blazor Server / SignalR), so the downstream worker sees work arrive without refreshing.

**Capacity note:** the default is one station per stage (a classic relay). The same model supports *multiple* stations sharing a stage for extra throughput — the atomic claim guarantees two workers at the same stage never grab the same unit. Whether the prototype exposes multi-station-per-stage is an open question (§11).

---

## 7. Functional requirements

### 7.1 Workflow builder (admin)
- FR-1 Create, rename, duplicate, archive a Workflow.
- FR-2 Add / edit / delete / reorder Stages within a Workflow.
- FR-3 Add / edit / delete / reorder Steps within a Stage, each with a name and instruction text.
- FR-4 Upload one or more images/GIFs per Step and set their display order.
- FR-5 "Duplicate workflow" so a new process can start from an existing one as a template.

### 7.2 Order / WorkItem management (admin)
- FR-6 Create a WorkItem manually with Order #, SKU, Qty, Channel, assigned to a chosen Workflow.
- FR-7 (Optional, nice-to-have) Bulk import WorkItems from a pasted list or CSV to mimic the old "Orders" sheet.
- FR-8 View the queue and current position of all WorkItems.

### 7.3 Station runtime & relay (worker)
- FR-9 A station is bound to one Stage of a Workflow; its screen shows the unit currently being worked, with the order header (Order #, SKU, Qty, Channel) and that stage's sub-steps.
- FR-10 The current sub-step is highlighted and its image(s) shown; advancing reveals the next sub-step.
- FR-11 Advancing is triggered by a keypress (e.g. Enter) **and** an on-screen button, with a short debounce to prevent double-advances (the old system tried this with a 5s lockout).
- FR-12 On advancing a sub-step, record a `StepCompletion` with a server timestamp.
- FR-13 When a station is free, it claims the oldest Queued unit at its stage via an **atomic claim** so two stations can never take the same unit.
- FR-14 On completing the last sub-step of a stage, the unit is **handed off** in one transaction: moved into the next stage's queue (status Queued, claim cleared); after the final stage it is marked Completed.
- FR-15 The station shows its **inbound queue depth** (how many units are waiting) so a worker can see the line backing up or running dry.
- FR-16 When a unit arrives in a stage's queue, the relevant station screen(s) update in **real time** without a manual refresh.

### 7.4 Timing & review (basic, prototype-level)
- FR-17 Persist all step completion timestamps.
- FR-18 Provide a simple view of completed orders with per-stage durations. (No charts required for the prototype.)

### 7.5 Self-containment
- FR-19 App runs locally and stores everything (definitions, orders, timings, uploaded images) in a single local database / local file area.
- FR-20 No external network dependency required to run the core flow.

---

## 8. Non-functional requirements

- **NFR-1 Tech:** C# / ASP.NET Core. (See §9.)
- **NFR-2 Self-contained DB:** local file database for the prototype, accessed through an ORM so the provider can be swapped later.
- **NFR-3 Portability of data layer:** business logic must not be tied to a specific database; switching to the company database later should be a configuration/provider change, not a rewrite.
- **NFR-4 Concurrent relay safety:** with several stations acting at once, claiming a unit and handing it off must be atomic and transactional. The claim uses a guarded conditional update plus an optimistic concurrency token so a unit can never be claimed by two stations, lost in hand-off, or duplicated. This is the central correctness requirement of the relay model.
- **NFR-5 Glanceability:** station screens use large text and images, readable from a working distance.
- **NFR-6 Responsiveness:** a step advance should reflect on screen within a fraction of a second.
- **NFR-7 Maintainability:** clear separation of data model, business logic, and UI so the workflow model can evolve without breaking the runtime.
- **NFR-8 Networked multi-client:** the app runs as one server on the LAN serving multiple station PCs concurrently over the network; all stations operate on the same live workflow and database simultaneously.
- **NFR-9 Cross-machine real-time:** a hand-off or state change at one station propagates to the affected station screens on other PCs within a fraction of a second, with no manual refresh and no file syncing.
- **NFR-10 LAN reachability:** the server is addressable by all stations via a stable hostname/IP; the system requires the LAN but no internet access.

---

## 9. Proposed technical approach

**Recommended stack: ASP.NET Core 8 + Blazor Server + EF Core + SQLite.**

| Layer | Choice | Rationale |
|-------|--------|-----------|
| Language / framework | C# / ASP.NET Core 8 | Matches your existing skill set; mature, well-documented web framework. |
| UI | Blazor Server | Whole app (UI + logic) in C# — no separate JS frontend to maintain, which was a key weakness of the old tool. **Server-hosted with a live SignalR connection per browser — purpose-built for many networked station PCs sharing one live workflow** with real-time screen updates. |
| Data access | Entity Framework Core | ORM that abstracts the database. Lets the prototype use SQLite now and swap to the company DB later by changing the provider + connection string. |
| Database (prototype) | SQLite (with WAL mode) | Single self-contained file on the server. Single-writer, but writes here are short and infrequent per station (a claim, a timestamp, a hand-off), so a handful of LAN stations is well within capacity. Write-ahead logging keeps readers non-blocking. |
| Media storage | Local file system folder on the server, referenced from `MediaAsset` rows | Simple, self-contained; served to all stations by the same server; avoids storing binaries in the DB for the prototype. |

**Trade-off to note:** Blazor Server requires a live connection between each browser and the server. On a shop-floor LAN this is exactly the intended model and a strength, not a limitation — every station is a thin browser client and the server is the single source of truth. If write contention ever exceeds SQLite's single-writer limit (many stations, heavy load), moving to SQL Server / PostgreSQL is a provider + connection-string change through EF Core, not a rewrite.

**Considered alternatives:**
- *ASP.NET Core Web API + React/JS frontend* — more flexible/decoupled, but adds a second language and more moving parts than a prototype needs.
- *Blazor WebAssembly* — runs in the browser and can be statically hosted, but database access is more awkward; Server is simpler for a DB-backed line tool.

### 9.1 Deployment & network topology

FlowLine is a **client–server LAN application**, not an app installed separately on each PC. There is exactly one running instance.

- **One server** (a designated PC or small box on the shop-floor network) runs the FlowLine application, holds the single SQLite database file, and stores the uploaded media. It is the only source of truth.
- **Each station is a networked PC** that simply opens a browser to the server's address (e.g. `http://flowline:5000`), selects which stage it is running, and works. No FlowLine install on the station itself — just a browser.
- **All stations share the same live workflow at the same time.** Because every browser holds a SignalR connection back to the one server, a hand-off at one station pushes an instant update to the next station's screen across the network, with no file syncing between machines.
- **The admin PC** is just another browser client pointing at the same server, used for the workflow builder and order entry.

```
                    ┌──────────────────────────────┐
                    │   Server PC (one, on LAN)     │
                    │   FlowLine app                │
                    │   SQLite DB + media folder    │
                    └───────────────┬──────────────┘
                                    │  LAN (HTTP + SignalR)
     ┌───────────────┬─────────────┼─────────────┬───────────────┐
 Station PC 1    Station PC 2   Station PC 3   Station PC 4    Admin PC
 (Stage 1)       (Stage 2)      (Stage 3)      (Stage 4)      (builder)
 browser         browser        browser        browser         browser
```

**Implications:**
- The server must be reachable by all station PCs (a fixed LAN IP or hostname).
- "Self-contained" = no cloud/external dependency and one machine hosts everything — *not* an isolated copy per PC.
- Adding a station is just opening a browser on another networked PC; no software rollout.
- Backup/portability = copy the single SQLite file plus the media folder from the server.

---

## 10. Prototype milestones

| Milestone | Outcome |
|-----------|---------|
| M0 — Skeleton | ASP.NET Core + Blazor Server project runs; EF Core + SQLite wired up; empty domain model migrates and creates the DB file. |
| M1 — Domain & seed | Workflow / Stage / Step / MediaAsset / WorkItem / StepCompletion entities exist; one workflow seeded that reproduces the original 4-step gaming-PC build. |
| M2 — Station runtime & relay | A worker at a station sees the unit at their stage, advances with key/button (timestamped), and on finishing the stage hands the unit to the next stage's queue while pulling its next unit. Multiple stations **on separate networked PCs** run concurrently against the one server with atomic claiming — proven by running two stations on different machines at once without a unit being double-claimed or lost, and confirming a hand-off on one PC appears live on the next. |
| M3 — Workflow builder | Admin can create/edit/reorder stages and steps and upload images entirely through the UI; can duplicate a workflow. |
| M4 — Second process | A genuinely different workflow (e.g. laptop disassembly) is created through the builder with no code changes — proving the model. |
| M5 — Timing review | Basic completed-orders view with per-stage durations. |

A successful prototype = **M4 reached**: a second, structurally different process running through the same engine, authored without touching code.

---

## 11. Open questions

1. **Multiple stations per stage:** the relay defaults to one station per stage. Should the prototype also allow *two or more* workers on the same stage for extra throughput? (The atomic claim already makes this safe; this is just whether to expose it in the UI.)
2. **Operator tracking:** Do we need to know *which worker* completed a step (for timing/accountability), or just which station? Stations are tracked regardless.
3. **Station setup:** How are stations configured and identified — a one-time admin setup screen, or does a screen pick its stage from a dropdown on load? (e.g. "this monitor is Build Line A / Stage 2".)
4. **Order source:** For the prototype, manual entry vs. CSV paste — is CSV import worth including in v1?
5. **Branching:** Do any real processes need conditional/branching steps (e.g. "if SKU = X, skip stage 3"), or is every workflow strictly linear? (Linear is assumed for the prototype.)
6. **Rework / send-back:** Can a downstream worker reject a unit and send it *back* upstream (failed QA), or does work only ever move forward? (Forward-only is assumed for now.)
7. **Media:** Re-host the existing GIFs locally, or start fresh? (The old ones are on Google Drive and won't be self-contained.)

**Resolved by the relay requirement:** the line is staffed by multiple concurrent workers, one per stage by default; stations are fixed to a stage; and work passes forward down the line via per-stage queues.

**Resolved by the networking requirement (§9.1):** deployment is one server on the LAN with multiple station PCs as browser clients sharing the same live workflow — not an isolated install per machine.

---

## 12. Appendix — mapping old → new

| Legacy concept | FlowLine equivalent |
|----------------|--------------------|
| Hardcoded `page1`–`page4` HTML files | One `Stage` runtime view, data-driven |
| Sheet tabs `Step1`–`Step4` | `Stage` rows under a `Workflow`, each manned by a `Station` |
| One row (row 2) per step-tab = one unit per step | Per-stage queue (WorkItems with `CurrentStage` = that stage), can buffer many |
| "1.1 Pick Case" etc. text in HTML | `Step` rows with name + instructions |
| Google Drive image URLs | `MediaAsset` rows → local files |
| Timestamp written to next empty column | `StepCompletion` row with timestamp |
| Row moved `Orders → Step1 → …` and deleted (unsafe) | Atomic claim + transactional hand-off (`CurrentStage` update, claim cleared) |
| `Orders Built Today` log | Completed `WorkItem`s + their `StepCompletion`s |
