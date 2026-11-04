# ADR-0004: Blazor Server as the Operator Dashboard

- **Status:** Accepted
- **Date:** 2026-11-04
- **Decision Makers:** OEE-Sentinel Development Team

## Context

OEE-Sentinel's UI is *operational* — used by plant operators, production engineers, and managers. It must show:

- A file upload form for shift logs (drag-and-drop, validation feedback, progress).
- Gauges for A / P / Q / OEE per machine and per shift.
- A Pareto chart of downtime reasons.
- A table of recent `LogIngestion` records with status (`PENDING → PROCESSING → COMPLETED | FAILED`).
- Navigation between machines and shifts.

The UI does **not** need: offline support, a mobile-native app, a PWA, or a complex SPA router. It needs to be **correct**, **readable**, and **maintainable by a single developer** (the same developer who wrote the backend).

The Visão Geral document (§Tecnologias) explicitly states "Blazor (com interatividade de servidor), permitindo partilha nativa de modelos C# entre a interface e o motor de negócio". The BYOP submission requires the project to be "active and real" (`(Ext) BYOP - Project submission instructions.pdf` p.2) — a working, deployed UI is part of that signal.

## Decision

We adopt **Blazor Server** (ASP.NET Core interactive server components, SignalR circuit under the hood) as the UI technology, hosted inside `OEESentinel.WebUI` alongside minimal-API REST endpoints.

Concretely:

1. **`Components/Pages/`** — Razor components (`Home.razor`, `Upload.razor`, `MachineDetail.razor`, `Pareto.razor`, `Ingestions.razor`, `Error.razor`, `NotFound.razor`). The template's `Counter.razor` and `Weather.razor` are **removed** in the first cleanup commit.
2. **`Components/Layout/`** — `MainLayout.razor` (sidebar nav) and `NavMenu.razor`. Bootstrap 5 is already present in `wwwroot/lib/bootstrap/` and is kept.
3. **`Components/`** — shared components: `OeeGauge.razor` (Chart.js doughnut), `ParetoChart.razor` (Chart.js horizontal bar), `ShiftSummaryCard.razor`, `IngestionStatusBadge.razor`.
4. **Interactivity mode:** **`InteractiveServer`** rendered globally (`<Routes @rendermode="InteractiveServer" />` in `App.razor`). No per-page opt-in mixing.
5. **Chart rendering** via `Chart.js` (loaded from `wwwroot/lib/chartjs/`) wrapped by a small `IJSRuntime` interop service (`Charts.razor.js` + `ChartJsInterop.cs`). No third-party Blazor chart wrapper (e.g., `ChartJs.Blazor`), to avoid a dependency that would need to keep pace with .NET 10.
6. **REST API** is exposed via `MapGet`/`MapPost` minimal APIs in `Program.cs` (or a `Controllers/` folder if it grows), returning DTOs from `Application`. It is independent of the Blazor circuit — a curl or Bruno client can hit `/api/v1/machines/{code}/oee` without a websocket.

## Considered Alternatives

### 1. Blazor WebAssembly (client-side)

- **Description:** Compile C# to WebAssembly, ship the .NET runtime to the browser, run the UI entirely client-side.
- **Pros:** No SignalR circuit; UI remains interactive if the server is briefly unavailable; no server-side state per user.
- **Cons:** WASM downloads are ~2–4 MB on first load (mitigated by AOT/brotli but still significant). More importantly, WASM has **no server-side rendering** of the initial page — the first paint shows a loading spinner until the runtime boots. For an operator in a factory who needs a glance at the dashboard, that is a UX regression. Also, the baseline requires no offline; WASM's main advantage is wasted. Debugging WASM is slower in `dotnet watch`. Memory footprint per tab is higher.
- **Reason rejected:** Slower first paint, no benefit for a trusted-network self-hosted tool.

### 2. ASP.NET Core MVC + Razor Pages (classic server rendering)

- **Description:** Controller-per-page + Razor Views, one HTTP request per user interaction.
- **Pros:** Fully stateless on the server; each page load is independent. Very mature. No websocket.
- **Cons:** Every upload, gauge refresh, or Pareto filter is a full-page reload (or an AJAX fragment). The UX feels 2008-era compared to interactive server components. Model-sharing is manual (a `ShiftViewModel` per page). Form validation state and file-upload progress require AJAX scripting, which reintroduces the JS-bridge that Blazor avoids. KB §7.3 discusses MPA vs SPA and Blazor Server sits comfortably in between.
- **Reason rejected:** Worse interaction model; re-introduces a JS bridge for progress and validation.

### 3. ASP.NET Core + React (SPA via Vite)

- **Description:** Backend served by ASP.NET, frontend by a separate React SPA built with Vite.
- **Pros:** Very rich ecosystem; best-in-class component libraries; the industry-standard for dashboards.
- **Cons:** Requires two development stacks (C# + TS), two build systems, two deployment artifacts, and a REST contract between them (which duplicates DTOs unless a codegen step is added). For a **single developer**, this overhead is substantial. The Vaadin/Kendo/chart libraries that make React dashboards powerful are heavyweight. React re-render semantics and `useEffect` for chart lifecycle add complexity. And the BYOP reviewer (`(Ext) BYOP - Project submission instructions.pdf` p.3) reads code — a two-language project is more to read, not less.
- **Reason rejected:** Two stacks for one developer; model-sharing lost.

### 4. Plain HTML + Vanilla JS + Chart.js (the "Sim, absolutamente!" pitch)

- **Description:** Razor/static `index.html` + vanilla JS to call the REST API and drive Chart.js.
- **Pros:** Zero build step, zero framework dependency. Smallest cognitive overhead.
- **Cons:** Manual DOM updates, manual fetch/error handling, manual state management for upload progress and dashboard filtering. Over 500 lines of `dashboard.js` this becomes the exact "Big Ball of Mud" that KB §7.1 warns against. On the OEE dashboard, displaying the gauges *and* the Pareto *and* the ingestion table *and* the machine selector creates a state-sharing problem that Blazor solves with plain C# properties, and that vanilla JS solves with increasingly fragile DOM manipulation.
- **Reason rejected:** Does not scale past two screens; loses C# model-sharing.

### 5. Blazor United (`.NET 8` Auto mode — mix of Server and WASM)

- **Description:** Per-component decision between Server and WASM rendering.
- **Pros:** Best of both worlds — static server components for content, WASM for interactive widgets.
- **Cons:** Over-engineered for the baseline. Requires maintaining both runtime hosting models. Debugging edge cases (e.g., a component that starts static and becomes WASM) is a known friction area. No baseline component needs WASM.
- **Reason rejected:** The baseline has zero components that benefit from WASM. Adding the capability adds complexity with no benefit.

## Consequences

### Positive

- **Model-sharing.** The same `ShiftOeeDto` returned by `GET /api/v1/machines/{code}/oee` is bound in `MachineDetail.razor` as `@shiftDto.Availability.Value` — no serializer, no codegen, no duplicate type definition. Refactoring a field in Application is caught by the compiler in the Razor component.
- **Server-side authority.** Every OEE value rendered in the UI is **server-computed**. The client cannot fabricate a number; the circuit only receives DTOs.
- **`dotnet watch` cycle.** Edit `OeeGauge.razor`, the browser updates within ~1 s. No `npm run dev` + `vite` + proxy bridge.
- **REST API is orthogonal.** A Bruno collection, a curl command, or a future MES client hits `/api/v1/...` and gets JSON. The Blazor circuit does not interfere. Verified by construction: the API endpoints are in `Program.cs`, the dashboard is in `Components/Pages/`, and they share only the Application DTOs.
- **Deployment is one `dotnet run`.** `docker compose up -d` for the DB, `dotnet run --project src/OEESentinel.WebUI` for the app. No frontend build. This is a direct signal for BYOP "how to run" (`(Ext) BYOP - Project submission instructions.pdf` p.3).
- **KB §15 alignment.** Blazor Server is a server-rendered component model with **strongly-typed** components; it is technically a "smart components + thin server" model that avoids the SPA state-management complexity that KB §15.1 warns against ("para estados locais… para estados globais…"). No state library needed.

### Negative / Trade-offs

- **SignalR circuit** — each interactive Blazor user consumes a websocket. For the target deployment (a plant server, tens of concurrent users) this is irrelevant. For a hypothetical external SaaS with thousands of users, the circuit count would matter. The BYOP deployment is not that.
- **Connection loss.** If the SignalR circuit drops, the UI must reconnect; `ReconnectModal.razor` (already in the template) handles this. This is a real UX consideration for a flaky factory Wi-Fi — mitigated by reconnection and by the fact that the API endpoints still work via HTTP without the circuit.
- **No offline mode.** The dashboard requires a live server. This is aligned with the self-hosted, single-plant deployment model; offline was explicitly out of scope.
- **Chart.js is a JS dependency.** `Chart.js` is loaded from `wwwroot/lib/chartjs/` and driven by `IJSRuntime`. The interop is small (`Charts.razor.js` ~60 lines) and stable.
- **Memory per user.** Server-side state per connected user is small (component tree, some DTOs) but non-zero. For a plant-scale deployment it is negligible.
- **Blazor debug feedback loop** is slightly slower than a pure static site; `dotnet watch` restarts faster than rebuilding an SPA but slower than editing a static HTML. Accepted.

## References

- KB §7.3 (MV* family, SPA vs MPA), §15 (frontend architecture, state management, REST communication).
- `Visão Geral do Projeto.txt` §Tecnologias (Blazor Server + Chart.js).
- `(Ext) BYOP - Project submission instructions.pdf` pp. 2–3 (UI as evidence of "active and real").
- `src/OEESentinel.WebUI/Components/Pages/`, `Layout/`, `wwwroot/lib/bootstrap/`.
- `README.md` ("Run the app", "Using the app").