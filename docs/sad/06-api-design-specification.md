# REST API Design Specification — OEE-Sentinel

## 1. Executive Summary and Contract-First Methodology

In compliance with ADR-0005 (Richardson L2) and ADR-0006 (immutable facts, pure calculator), this document establishes the design guidelines, transport conventions, and Data Transfer Object (DTO) contracts for the **OEE-Sentinel REST API**.

The API is a **secondary** interface: the primary consumer is the Blazor Server dashboard (which bypasses HTTP entirely — see `04-principles-and-guidelines.md` §4.2). The REST API exists for:

1. **External MES/ERP integration** — a plant's existing MES consumes shift-level OEE.
2. **Scripted / CI verification** — a `curl` or `Bruno` collection exercises the same endpoints without the Blazor circuit.
3. **Roadmap integration** — the Prometheus exporter (T5) will query `/api/v1/...` or scrape `/metrics`; the folder watcher (T3) will trigger `POST /api/v1/logs` internally.

Contract-first means:

- **OpenAPI 3.0** is the authoritative contract (`docs/api/openapi.yaml`, generated from annotations on `Program.cs` — roadmap T8).
- **No consumer-specific endpoints.** No `/api/blazor/...` or `/api/spa/...`. One API surface, one version.
- **No unpublished endpoints.** Every endpoint in `Program.cs` is in the OpenAPI spec. Every endpoint in the spec is implemented. A review check.

---

## 2. Global Conventions

### 2.1. Protocol and Content Negotiation

| Aspect | Convention |
|---|---|
| **Base path** | `/api/v1` (versioned in path, not header) |
| **Transport** | HTTPS in production; HTTP only for `localhost` local dev |
| **Serialization** | `application/json; charset=utf-8` exclusively |
| **Casing (URIs)** | `kebab-case` (`/api/v1/machines`, `/api/v1/downtime-reasons`) |
| **Casing (JSON)** | `camelCase` (`windowStart`, `totalParts`, `sourceIngestionId`) |
| **Enums** | `UPPER_SNAKE_CASE` strings (`PLANNED`, `UNPLANNED`, `COMPLETED`, `FAILED`) |
| **Dates** | ISO 8601 with `Z` suffix (`2026-11-04T08:00:00Z`); query params accept `date` or `datetime` |
| **Duration** | Serialized as seconds (`double`), e.g., `downtimeSeconds: 1200.0` |
| **Percentages** | Serialized as `[0,1]` `decimal(5,4)`, not `0-100` (client multiplies at display) |

### 2.2. HTTP Method Semantics

| Method | Semantic | Idempotent | Baseline endpoints |
|---|---|---|---|
| `GET` | Read a resource or collection | Yes | `GET /api/v1/machines`, `GET /api/v1/machines/{code}/oee` |
| `POST` | Create a resource or trigger a process | No | `POST /api/v1/logs` (upload) |
| `PUT` | Replace a resource | Yes | *(none in baseline; reserved for T2's machine config update)* |
| `PATCH` | Partial update | No | *(none in baseline)* |
| `DELETE` | Remove or deactivate a resource | Yes | *(none in baseline; reserved for T2's soft-deactivate `Machine`)* |

### 2.3. Roles and Authorization (Baseline vs Planned)

- **Baseline:** *No authentication.* The API is open on the local network. This is a **deliberate trust-boundary decision** (ADR-0002 §Context) — the tool is deployed on a plant's internal server, accessed by authenticated network users. The alternative (auth from day one) would delay the baseline by 4h and contradict BYOP's "active and real" emphasis on the working slice.
- **Planned (T2):** JWT authentication + role enforcement (`OPERATOR`, `ENGINEER`, `ADMINISTRATOR`). Endpoints in §3 below carry a `Role` column marked `TODO (T2)`.
- **Deferred decision (T2 ADR):** Cookie-based vs Bearer-based token transport for the Blazor circuit. Both are compatible with the API contract; only transport changes.

---

## 3. Baseline Endpoint Catalogue

### 3.1. Log Ingestion

#### `POST /api/v1/logs`

**Purpose:** Upload a production log file, parse it, compute the `ProductionShift`, persist both, return the ingestion status.

**Request (multipart/form-data):**

| Field | Type | Required | Notes |
|---|---|---|---|
| `file` | binary | Yes | CSV/TXT, UTF-8, `Content-Type` detected by extension |
| `machineCode` | string | Yes | Must match an existing `Machine.code` (case-insensitive) |
| `windowStart` | ISO datetime | Yes | Shift window start (UTC) |
| `windowEnd` | ISO datetime | Yes | Shift window end (UTC); must be `> windowStart` |

**Response `201 Created`:**

```json
{
  "ingestionId": "018e9c7e-6c2a-7b8e-9a3d-4f1c7e2b5d10",
  "status": "COMPLETED",
  "machineCode": "MAQ-01",
  "window": {
    "start": "2026-11-04T08:00:00Z",
    "end":   "2026-11-04T16:00:00Z"
  },
  "rowsProcessed": 184532,
  "rowsRejected": 217,
  "rejectedSample": [
    {
      "lineNumber": 47,
      "rawLine": "2026-11-04T08:03:12,MAQ-01,,ERR_OK",
      "reason": "MISSING_QUANTITY"
    },
    {
      "lineNumber": 812,
      "rawLine": "2026-11-04T09:01:44,MAQ-01,15.5,ERR_OK",
      "reason": "INVALID_DECIMAL_LOCALE"
    }
  ],
  "productionShiftId": "019e2b1a-7d3f-7c4e-8b2f-5a6d8e0c4f12",
  "completedAt": "2026-11-04T16:02:11Z"
}
```

**Response `202 Accepted`** — *reserved for roadmap T3 (folder watcher).* When the ingestion is triggered asynchronously, the response body is the `ingestionId` and the status is `PENDING`; the client polls `GET /api/v1/logs/{id}`.

**Error responses:**

| Status | Condition | Body |
|---|---|---|
| `400` | Missing `file` or `machineCode`, `windowEnd <= windowStart` | `ProblemDetails` |
| `404` | `machineCode` does not match an active `Machine` | `ProblemDetails` |
| `409` | A non-superseded `ProductionShift` already exists for the same `(machineCode, window)` | `ProblemDetails` (with existing shift id) |
| `422` | Domain invariant failure: `planned == 0` or `downtime > planned` (ADR-0006) | `ProblemDetails` |
| `500` | Unhandled exception | `ProblemDetails` (no stack trace) |

> **`409` semantics.** Baseline policy: a duplicate shift for the same `(machine, window)` is rejected, not silently accepted. Corrections are an explicit operation (future: `POST /api/v1/logs/{id}/correct` under T4). Until then, a correction requires deleting the shift (future admin endpoint, T2) or using a fresh `windowEnd` slightly offset — both are documented as known limitations.

#### `GET /api/v1/logs/{ingestionId}`

**Purpose:** Poll ingestion status (for async path, T3).

**Response `200 OK`:**

```json
{
  "ingestionId": "018e9c7e-6c2a-7b8e-9a3d-4f1c7e2b5d10",
  "machineCode": "MAQ-01",
  "status": "COMPLETED",
  "uploadedAt": "2026-11-04T16:01:59Z",
  "completedAt": "2026-11-04T16:02:11Z",
  "rowsProcessed": 184532,
  "rowsRejected": 217,
  "productionShiftId": "019e2b1a-7d3f-7c4e-8b2f-5a6d8e0c4f12",
  "failureReason": null
}
```

**Error responses:** `404` if the ingestion does not exist.

---

### 3.2. Machines

#### `GET /api/v1/machines`

**Purpose:** List active machines.

**Query parameters (all optional):**
- `search` — case-insensitive substring on `code` or `name`
- `page` (default `1`), `pageSize` (default `20`, max `100`)

**Response `200 OK`:**

```json
{
  "items": [
    {
      "code": "MAQ-01",
      "name": "Injection Press A1",
      "idealCycleSeconds": 2.5,
      "plannedShiftSeconds": 28800,
      "isActive": true
    }
  ],
  "page": 1,
  "pageSize": 20,
  "total": 12,
  "totalPages": 1
}
```

#### `GET /api/v1/machines/{code}`

**Purpose:** Retrieve one machine's configuration.

**Response `200 OK`:**

```json
{
  "code": "MAQ-01",
  "name": "Injection Press A1",
  "idealCycleSeconds": 2.5,
  "plannedShiftSeconds": 28800,
  "isActive": true,
  "createdAt": "2026-10-01T09:00:00Z",
  "updatedAt": "2026-10-01T09:00:00Z"
}
```

**Error responses:** `404` if not found.

> **`POST`, `PUT`, `DELETE /api/v1/machines[/{code}]`** are deferred to T2 (with auth + RBAC). Baseline machines are seeded.

---

### 3.3. OEE and Shifts

#### `GET /api/v1/machines/{code}/oee`

**Purpose:** Retrieve the aggregated OEE across a window. This is the primary analytical query for MES integration.

**Query parameters:**

| Name | Type | Required | Notes |
|---|---|---|---|
| `from` | ISO date/datetime | Yes | Inclusive |
| `to` | ISO date/datetime | Yes | Exclusive; `from < to`; span `≤ 90 days` |
| `includeSuperseded` | boolean | No (default `false`) | If `true`, includes superseded shifts in aggregate |

**Semantics:** Aggregates across all **non-superseded** `ProductionShift` records whose `window` is fully contained in `[from, to)`. The four percentages are computed as **totals-based** aggregation:
- `Availability = Σ OperatingTime / Σ PlannedTime`
- `Performance = Σ (TotalParts × IdealCycleSeconds) / Σ OperatingTime`
- `Quality = Σ ApprovedParts / Σ TotalParts`
- `OEE = A × P × Q` (recomputed from aggregates, not averaged)

> **Aggregation policy.** Averaging shift-level OEE values is a known statistical error (a shift with 100 parts and a shift with 100k parts are not equally weighted). The API computes from raw totals. This is documented as a deliberate choice — the dashboard does the same.

**Response `200 OK`:**

```json
{
  "machineCode": "MAQ-01",
  "window": {
    "from": "2026-10-28T00:00:00Z",
    "to":   "2026-11-04T00:00:00Z"
  },
  "shiftsIncluded": 21,
  "totals": {
    "plannedSeconds": 604800,
    "downtimeSeconds": 72000,
    "operatingSeconds": 532800,
    "totalParts": 184500,
    "approvedParts": 180210,
    "rejectedParts": 4290
  },
  "availability": 0.8809,
  "performance": 0.8654,
  "quality": 0.9767,
  "oee": 0.7447,
  "lastUpdatedAt": "2026-11-04T16:02:11Z"
}
```

**Error responses:**

| Status | Condition |
|---|---|
| `400` | `from >= to`, `to - from > 90 days`, or missing `from`/`to` |
| `404` | `machineCode` does not exist |
| `422` | No non-superseded shifts in window (empty aggregate is a domain-level "not computed yet" state) |

#### `GET /api/v1/machines/{code}/shifts`

**Purpose:** List individual `ProductionShift` facts for a machine.

**Query parameters:**
- `from`, `to` (required; same guard as `/oee`)
- `includeSuperseded` (default `false`)
- `page`, `pageSize` (default `20`, max `100`)

**Response `200 OK`:**

```json
{
  "items": [
    {
      "id": "019e2b1a-7d3f-7c4e-8b2f-5a6d8e0c4f12",
      "machineCode": "MAQ-01",
      "window": {
        "start": "2026-11-04T08:00:00Z",
        "end":   "2026-11-04T16:00:00Z"
      },
      "totals": {
        "totalParts": 8800,
        "approvedParts": 8600,
        "rejectedParts": 200
      },
      "downtimeBreakdown": [
        { "reasonCode": "PLANNED_BREAK",       "category": "PLANNED",   "seconds": 1800 },
        { "reasonCode": "UNPLANNED_MECHANICAL","category": "UNPLANNED", "seconds": 900  },
        { "reasonCode": "UNPLANNED_TOOL_CHANGE","category": "UNPLANNED","seconds": 300  }
      ],
      "availability": 0.9375,
      "performance": 0.8800,
      "quality": 0.9773,
      "oee": 0.8073,
      "sourceIngestionId": "018e9c7e-6c2a-7b8e-9a3d-4f1c7e2b5d10",
      "supersededBy": null,
      "recordedAt": "2026-11-04T16:02:11Z"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "total": 1,
  "totalPages": 1
}
```

#### `GET /api/v1/machines/{code}/pareto`

**Purpose:** Pareto analysis of downtime reasons across a window.

**Query parameters:** `from`, `to` (required; same guard).

**Response `200 OK`:**

```json
{
  "machineCode": "MAQ-01",
  "window": {
    "from": "2026-10-28T00:00:00Z",
    "to":   "2026-11-04T00:00:00Z"
  },
  "totalShifts": 21,
  "totalDowntimeSeconds": 72000,
  "entries": [
    {
      "reasonCode": "UNPLANNED_MECHANICAL",
      "category": "UNPLANNED",
      "description": "Mechanical failure",
      "seconds": 36000,
      "percentage": 0.50
    },
    {
      "reasonCode": "UNPLANNED_TOOL_CHANGE",
      "category": "UNPLANNED",
      "description": "Tool change",
      "seconds": 18000,
      "percentage": 0.25
    },
    {
      "reasonCode": "PLANNED_BREAK",
      "category": "PLANNED",
      "description": "Scheduled break",
      "seconds": 18000,
      "percentage": 0.25
    }
  ]
}
```

`entries` is sorted by `seconds DESC`. Ties are broken by `reasonCode ASC` (stable, testable).

---

### 3.4. Diagnostic

#### `GET /api/v1/health`

**Purpose:** Liveness + readiness probe for Docker/`docker compose` and future Prometheus blackbox exporter.

**Response `200 OK`:**

```json
{
  "status": "ok",
  "version": "0.1.0-baseline",
  "timestamp": "2026-11-04T16:02:11Z",
  "database": "connected",
  "uptimeSeconds": 86400
}
```

**Response `503 Service Unavailable`** if the database is unreachable (checked by a `SELECT 1` with a 2s timeout).

---

## 4. DTO Models and Domain Invariant Mapping

### 4.1. Invariant guarantees at the DTO boundary

Every DTO returned by the API is guaranteed by construction:

| DTO field | Guarantee | Enforced by |
|---|---|---|
| `availability`, `performance`, `quality`, `oee` | `∈ [0,1]` | `OeeCalculatorEngine` clamp + `Percentage` VO + DB CHECK |
| `totals.approvedParts ≤ totals.totalParts` | always | `ProductionTotals` constructor + DB CHECK |
| `totals.rejectedParts = totalParts - approvedParts` | always | computed property, not a constructor parameter |
| `downtimeBreakdown[].seconds ≥ 0` | always | `Duration` VO + DB CHECK on JSONB shape |
| `window.start < window.end` | always | `TimeRange` VO + DB CHECK |
| `supersededBy` | `null` for current shift; `UUID` for superseded | partial unique index in DB |

### 4.2. Request DTO validation

All request DTOs are validated at the boundary by a minimal-API `IEndpointFilter` or by explicit `ArgumentNullException.ThrowIfNull` + domain exceptions. The baseline uses:

- `[FromForm]` model binding for `POST /api/v1/logs` with a validation filter.
- `[FromQuery]` binding for `GET` endpoints with a `IDateRangeValidator` invoked explicitly.

**No `FluentValidation` in baseline** — the validation surface is small enough that a hand-written `Validator` class per DTO is simpler. Deferred to T6 if it grows.

---

## 5. Standard Error Envelope

All `4xx`/`5xx` responses use **RFC 9457 `ProblemDetails`** (ASP.NET Core's built-in support):

```json
{
  "type": "https://oee-sentinel.local/errors/machine-not-found",
  "title": "Machine not found",
  "status": 404,
  "detail": "Machine 'MAQ-99' does not exist or has been deactivated.",
  "instance": "/api/v1/machines/MAQ-99/oee",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
}
```

**Mapping table:**

| Domain exception / condition | HTTP status | `type` URI |
|---|---|---|
| `MachineNotFoundDomainException` | `404` | `.../machine-not-found` |
| `LogIngestionNotFoundDomainException` | `404` | `.../ingestion-not-found` |
| `InvalidWindowDomainException` (`from >= to`, `span > 90d`) | `400` | `.../invalid-window` |
| `DuplicateShiftDomainException` (409 condition) | `409` | `.../shift-already-exists` |
| `PlannedTimeZeroDomainException` | `422` | `.../planned-time-zero` |
| `DowntimeOverflowDomainException` | `422` | `.../downtime-overflow` |
| Empty aggregate (`GetMachineOeeQuery` returns 0 shifts) | `422` | `.../no-shifts-in-window` |
| Unhandled exception | `500` | `about:blank` (generic) |

**Trace id** is included from `Activity.Current.Id` (OpenTelemetry-compatible). No stack traces in `500` responses — logged server-side only.

---

## 6. Versioning, Deprecation, and Evolution

- **Versioning policy.** URL path (`/api/v1`). v1 is frozen at submission. A future v2 is a new route group.
- **Additive changes** (new optional fields, new endpoints) are allowed within v1 without a version bump.
- **Breaking changes** (removing fields, changing types, changing semantics) require `/api/v2`.
- **Deprecation.** Deprecated fields/endpoints emit a `Deprecation: true` and a `Sunset` HTTP header (RFC 8594). Not used in baseline.
- **OpenAPI generation** (roadmap T8). Baseline: hand-authored `docs/api/openapi.yaml` (or generated via `Swashbuckle.AspNetCore` — `dotnet add package Swashbuckle.AspNetCore` — and checked into the repo for review).

---

## 7. Blazor Server — same endpoints, different path

For completeness:

- The Blazor dashboard does **not** call the REST API. It injects the Application handlers directly (`IGetMachineOeeHandler`, `IProcessLogFileHandler`, `IGetDowntimeParetoHandler`).
- The REST API is reserved for **external clients**. A component that calls `/api/v1/...` via `HttpClient` inside a Blazor circuit is a code smell — the in-process handler is faster, avoids a round-trip, and is type-checked end-to-end.
- The only exception is the `GET /api/v1/health` endpoint, which the Blazor app does not call (it uses direct DI).

This dual-path design is documented in `04-principles-and-guidelines.md` §4.2 and `05-key-scenarios.md`.

---

## 8. Comparative Log

### Section 3.3 (`GET /machines/{code}/oee`) — aggregation policy

- **Initial:** "Average the shift-level OEE values across the window."
- **Current:** "Compute from aggregates: `Σ OperatingTime / Σ PlannedTime`, etc."
- **Justification:** Averaging percentages is statistically incorrect when shifts have different weights. A 100-part shift and a 100k-part shift must not weigh equally. Documented in §3.3 and enforced in the Application handler's `AggregateOeeFromShifts` method (unit-tested).

### Section 3.1 (`POST /logs`) — 409 semantics

- **Initial:** "Duplicate window is silently superseded."
- **Current:** "Baseline returns `409 Conflict`. Corrections are an explicit operation (future `POST /logs/{id}/correct`)."
- **Justification:** Silent supersession hides a likely user error (wrong file for wrong machine). An explicit 409 forces the caller to acknowledge. The correction workflow (T4) is planned.

### Section 5 — ProblemDetails vs custom envelope

- **Initial:** "Custom error envelope with `timestamp`, `status`, `error`, `message`, `details`."
- **Current:** "RFC 9457 `ProblemDetails`."
- **Justification:** `ProblemDetails` is built into ASP.NET Core, OpenAPI-annotated out of the box, and interoperable with any HTTP client. A custom envelope reimplements the standard badly.