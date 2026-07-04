# Workflow Customisation Plan

Status: **in progress**.

Built in this working tree:
- Stations are visible/editable inside the workflow builder.
- Workflow list and editor show runnable/not-runnable readiness warnings.
- Scan verification moved from steps to stages. A scan-required stage is fed by scanning an order number; it does not auto-claim.
- **Manager-configured step inputs** (Text / Number / Pass-Fail / Checklist): the manager adds
  any number of inputs to a step; the operator records them (required inputs gate Advance,
  Number is validated); answers persist as `StepCompletionValue` rows and show on a per-unit
  detail view at `/admin/orders/{id}`. Verified end to end in the browser.

Still planned:
- Tier 2 ergonomics (duplicate a single stage/step, reorder media, richer instructions) and
  Tier 3 (branching, SKU auto-routing, per-step target times).

## Current Builder Surface

- **Workflow:** name, description, active/archived, requires prebuild, free-run/training mode.
- **Stages:** add, rename, reorder, delete, requires scan-to-claim, inline station add/rename/delete, readiness warnings.
- **Steps:** name, instructions, reorder, delete, and manager-configured data-capture inputs.
- **Media:** upload/delete images or GIFs per step. Media reorder is still not built.
- **Stations:** still available on `/admin/stations`, now also manageable from each stage in `/admin/workflows/{id}`.

## Built: Stations And Readiness

A workflow is considered runnable only when:
- It is active.
- It has at least one stage.
- Every stage has at least one step.
- Every stage has at least one station.

The workflow list shows a Ready / Not ready badge. The editor shows the detailed reasons, plus inline stage warnings for missing stations or missing steps.

## Built: Stage-Level Scan To Claim

`Step.RequiresScan` has been removed. `Stage.RequiresScan` replaces it.

When a stage requires scan:
- A station at that stage does not auto-claim the oldest queued unit.
- The station shows a scan box while idle.
- The operator scans the unit's order number.
- `RelayService.ClaimByScanAsync` claims the oldest queued WorkItem at that stage with the matching `OrderNumber`.
- Once claimed, the operator works the steps normally; no additional step scan is required.

Prebuild stage 1 remains its own mode: it can create units from `History` or claim an already queued unit. Downstream scan-required stages use `ClaimByScanAsync`.

## Next: Step Data Capture

The next major capability is manager-configured inputs per step.

Planned model:
- `StepInput`: `Id`, `StepId`, `Label`, `Type`, `Required`, `OrderIndex`, `Options`.
- `StepCompletionValue`: `Id`, `StepCompletionId`, `StepInputId`, `Value`.

Planned input types:
- Text
- Number
- Pass / Fail
- Checklist

Planned behavior:
- The builder gets an Inputs section under each step.
- Managers can add multiple inputs to a step, choose the type, mark required, reorder, and delete.
- Station runtime renders the configured inputs for the current step.
- `AdvanceAsync` validates required inputs and number parsing.
- Captured values are saved with the `StepCompletion`.
- `/admin/orders` gets a per-unit detail view showing completed steps and captured values.

## Later Ergonomics

- Duplicate a single stage or step.
- Reorder media.
- Drag/drop stage and step ordering.
- Richer instructions: formatting, links, PDFs, video.
- Optional target time per step for stats/timing comparisons.
