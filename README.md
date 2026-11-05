# OEE-Sentinel

Industrial production log analysis and **OEE (Overall Equipment Effectiveness)** engine for manufacturing plants and legacy assembly lines.

OEE-Sentinel ingests raw production logs (CSV/TXT) from industrial machines, normalizes them into a machine-agnostic domain model, and computes the three OEE pillars — **Availability**, **Performance**, and **Quality** — plus Pareto analysis of downtime causes. It ships with a Blazor Server dashboard for operators/engineers and a REST API for MES/ERP integration.

## Who it's for

- **Plant managers** tired of monthly OEE spreadsheets.
- **Production engineers** who need to trace every OEE figure back to a source file.
- **Legacy workshops** with machines that write text logs but no MES/SCADA.

## What it's built with

| Layer | Technology |
|---|---|
| Runtime | C# / **.NET 10** |
| Architecture | Clean / Onion Architecture, DDD tactical patterns |
| Web UI | **Blazor Server** + Chart.js |
| REST API | ASP.NET Core Minimal APIs / Controllers |
| Persistence | **PostgreSQL 16** + Entity Framework Core + `EFCore.BulkExtensions` |
| Parsing | `CsvHelper` with `IAsyncEnumerable<ProductionEvent>` (streaming) |
| Testing | **xUnit** + **FluentAssertions** |
| Infrastructure | Docker Compose (PostgreSQL), `.env.example` |

## How to run it

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for PostgreSQL)

### Quickstart

```bash
# 1. Clone
git clone https://github.com/<you>/oee-sentinel.git
cd oee-sentinel

# 2. Start PostgreSQL
docker compose up -d

# 3. Apply migrations
dotnet ef database update \
  --project src/OEESentinel.Infrastructure \
  --startup-project src/OEESentinel.WebUI

# 4. Run the app
dotnet run --project src/OEESentinel.WebUI
```

Then open **http://localhost:5278**.

### Run the tests

```bash
dotnet test
```

## Using the app

1. Navigate to **/upload**.
2. Drop a CSV file with the canonical schema (see `samples/sample_shift.csv`).
3. Select the machine and shift window.
4. The dashboard renders Availability / Performance / Quality gauges and a Pareto of downtime reasons.

## Project structure

```
oee-sentinel/
├── src/
│   ├── OEESentinel.Domain/          # Pure business rules (entities, VOs, OeeCalculatorEngine)
│   ├── OEESentinel.Application/     # Use cases, commands, queries, ports
│   ├── OEESentinel.Infrastructure/  # EF Core, CsvHelper, parser adapters
│   └── OEESentinel.WebUI/           # Blazor Server, REST controllers, wwwroot
├── tests/
│   └── OEESentinel.UnitTests/       # xUnit + FluentAssertions
├── docs/                            # Problem statement, context, glossary, domain model, ADRs
├── samples/                         # Sample log files (fictitious data)
├── docker-compose.yml
├── ROADMAP.md
├── LICENSE
└── README.md
```

## Documentation

- [`docs/problem-statement.md`](docs/problem-statement.md) — What problem OEE-Sentinel solves and why.
- [`docs/context.md`](docs/context.md) — Industrial benchmark, stakeholders, quality drivers.
- [`docs/glossary.md`](docs/glossary.md) — Ubiquitous language and architectural glossary.
- [`docs/domain-model.md`](docs/domain-model.md) — Tactical DDD design (aggregates, VOs, invariants).
- [`ROADMAP.md`](ROADMAP.md) — Baseline scope + 20h+ planned work.

## License

MIT — see [`LICENSE`](LICENSE).

## Status

Baseline implementation in progress. See [`ROADMAP.md`](ROADMAP.md) for the current milestone and the 20h+ planned roadmap (PDF export, JWT/RBAC, file watcher, Prometheus, etc.).