# ADR-0005: Streaming Log Parsing via CsvHelper + IAsyncEnumerable

- **Status:** Accepted
- **Date:** 2026-11-04
- **Decision Makers:** OEE-Sentinel Development Team

## Context

Industrial machines emit CSV/TXT logs in a format documented per machine — typically `timestamp,machine_code,quantity,error_code` with no header, per `docs/sad/02-context.md` §2. Volume is 5k–200k lines per shift per machine (baseline); a target of 200k lines must not exceed 200 MB peak RSS (per `docs/sad/01-problem-statement.md` §3's performance driver).

Three failure modes are explicitly in scope for the baseline:

1. **Malformed lines** must be rejected individually. One bad row (`missing quantity field`, `bad decimal locale`, `truncated timestamp`) must not abort the entire ingestion. The line number and cause must be recorded against the `LogIngestion` (`docs/domain/domain-model.md` §2.3).
2. **Locale mismatch** in decimals (`.` vs `,`) is common in mixed-vendor plants. The parser must accept the canonical `.` and record others as rejected lines with a specific error.
3. **Extremely long fields** (a stray 10MB string in an error-code column, or an embedded NUL byte) must not crash the parser. CsvHelper handles this gracefully, but the domain `ProductionEvent` construction must not.

The parser port is `IProductionLogParser.ParseAsync(Stream, CancellationToken) → IAsyncEnumerable<ProductionEvent>`, declared in Application (ADR-0001). The parser implementation is `CsvProductionLogParser` in Infrastructure. The port is deliberately **pull-based** (`IAsyncEnumerable`), not push-based, because `ProcessLogFileCommand` should be able to `await foreach` and short-circuit at any line (e.g., if the machine was deactivated mid-ingestion).

## Decision

We adopt **`CsvHelper`** with the **`IAsyncEnumerable<ProductionEvent>`** consumption model for the baseline CSV parser.

Concretely, in `Infrastructure/Parsing/CsvProductionLogParser.cs`:

1. **Class map declared via `ClassMap<ProductionEventRow>`** (a flat DTO mirroring the CSV row, not the domain `ProductionEvent` directly — the domain type is constructed from the row, applying domain invariants).
2. **Configuration:**
   - `HasHeaderRecord = false` (industrial logs do not always have headers; we ship a canonical sample with an optional header, but the parser accepts both).
   - `Delimiter = ","`; `BadDataFound = null` (reject the line, do not throw).
   - `MissingFieldFound = null` (reject the line, do not throw).
   - `TrimOptions = TrimOptions.Trim`.
   - `CultureInfo.InvariantCulture` — **always**. This is the source of the decimal-locale policy: `.` accepted, `,` recorded as a rejected line with `ErrorCode = "INVALID_DECIMAL_LOCALE"`.
3. **Reader:** `using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024, leaveOpen: false)`.
   The stream is **never read into a `string` or a `List<>`**. The `StreamReader` buffers 64 KB; CsvHelper advances it row by row.
4. **Consumption:** 
   ```csharp
   public async IAsyncEnumerable<ProductionEvent> ParseAsync(
       Stream source,
       [EnumeratorCancellation] CancellationToken ct = default)
   {
       using var reader = new StreamReader(source, /* … */);
       using var csv = new CsvReader(reader, config);
       csv.Context.RegisterClassMap<ProductionEventRowMap>();

       await foreach (var row in csv.GetRecordsAsync<ProductionEventRow>(ct))
       {
           if (!TryMapToDomain(row, out var evt, out var reason))
           {
               // record rejected line: line number, raw text, reason
               OnRejectedRow?.Invoke(reader.LineNumber, row, reason);
               continue;
           }
           yield return evt;
       }
   }
   ```
   The rejected-row callback is an `event` on the parser, consumed by `ProcessLogFileCommand`, which increments `LogIngestion.RowsRejected` and stores a bounded sample (max 100 rejected lines) in memory for the "why did the file fail?" report.
5. **No parallelism in the parser.** Parsing is inherently sequential (the CSV row order is significant for `downtime` computation, which is based on inter-event timestamps). Parallelism is *persistence-side*: `ProcessLogFileCommand` batches `ProductionEvent` into `IReadOnlyList<>` chunks of 5000 and `BulkInsertAsync` them via ADR-0003.
6. **Bounded rejected-sample buffer.** The rejected-rows log lives in `LogIngestion` as a JSONB column, capped at 100 entries — not the whole file.

## Considered Alternatives

### 1. `File.ReadAllLines` + `string.Split(',')`

- **Description:** Read the entire file into memory, split each line on `,`.
- **Pros:** Simplest possible code. 3 lines.
- **Cons:** **Violates the bounded-memory NFR by construction.** A 200k-line file at ~100 bytes/line is 20 MB just for the strings, plus the array overhead, plus the char[] allocations from `Split` — easily 200+ MB for a "modest" file. Larger files (500k lines, 50 MB) would OOM a container with 256 MB. Also does not handle quoted fields, escaped delimiters, or BOM. `ReadAllLines` on a network-mounted file blocks for seconds.
- **Reason rejected:** Fails the primary performance driver.

### 2. `StreamReader.ReadLine()` loop + manual split

- **Description:** Read line by line, split on `,`, construct `ProductionEvent`.
- **Pros:** Bounded memory (one line at a time). Simple. No library dependency.
- **Cons:** Reimplements CsvHelper's quoting, escaping, BOM, and line-number tracking — badly. A line with a quoted comma (`"machine, faulty",42`) would break. `LineNumber` for diagnostics becomes a manual counter. `IAsyncEnumerable` wrapper requires a `Task.Run` offload, which loses the pull-based cancellation semantics that the port promises. And the parsing *pace* is the developer's problem — an `await reader.ReadLineAsync()` per line is a syscall per line, far slower than CsvHelper's 64 KB buffered reader.
- **Reason rejected:** Reimplements a solved problem; slower than CsvHelper.

### 3. `CsvHelper` synchronous (`GetRecords<T>()`)

- **Description:** Use CsvHelper but with the synchronous API.
- **Pros:** Slightly simpler (no `await foreach`). Marginally faster in micro-benchmarks.
- **Cons:** Blocks the calling thread for the duration of parsing. For a Blazor Server upload, this blocks a threadpool thread for the duration of a 200k-line file (seconds). For a future `BackgroundService` (roadmap T3), synchronous parse on the worker thread is acceptable — but the *port* would then be synchronous, forcing every consumer to be synchronous. Async parity with the rest of the Application (EF Core's `SaveChangesAsync`, bulk insert's `BulkInsertAsync`) is the reason for the async port.
- **Reason rejected:** Async parity across the stack.

### 4. `Microsoft.VisualBasic.FileIO.TextFieldParser`

- **Description:** The VB.NET `TextFieldParser` — a surprisingly good CSV parser.
- **Pros:** Built into the .NET runtime (from VB, but usable from C#). Handles quoted fields.
- **Cons:** No `IAsyncEnumerable` support. API is VB-flavoured (Out parameters). No BOM handling as ergonomic as `StreamReader`. The `using` statements require `Microsoft.VisualBasic` reference which, while available, is a strange signal for a modern C# project. CsvHelper has a richer class-map model.
- **Reason rejected:** Awkward API; no async; awkward reference.

### 5. `Sep` and other high-performance CSV libraries

- **Description:** Use a span-based, zero-allocation CSV parser like `Sep` or `Sylvan.Data.Csv`.
- **Pros:** 2–5× faster than CsvHelper. Zero allocations for the row parsing.
- **Cons:** Steeper learning curve. `Sep`'s API is closer to `Span<T>` and `SequenceReader<byte>`, which is excellent for benchmark demonstrations but adds cognitive overhead for a maintainable baseline. CsvHelper's class-map and `GetRecordsAsync<T>()` are directly readable. For a 200k-line file, CsvHelper parses in ~1–2 seconds (verified per roadmap T7 target); 3× faster is ~0.5 s — not material to the UX. `Sep` has a smaller community than CsvHelper. Deferred until a real performance issue demands it.
- **Reason rejected:** Over-engineered for the actual data sizes; more learning cost than benefit.

### 6. Push-based parser (callback per row)

- **Description:** `void Parse(Stream, Action<ProductionEvent>, CancellationToken)`.
- **Pros:** Natural for synchronous parsers. Allows the parser to control pace.
- **Cons:** Backpressure is not expressible: the caller cannot pause consumption. Short-circuiting (e.g., on a cancelled cancellation-token) requires exceptions or a boolean return, both of which are awkward. The pull model is the natural expression of "the ingestion pipeline consumes events as fast as it can, up to the speed of persistence."
- **Reason rejected:** Pull-based `IAsyncEnumerable` is idiomatic for streaming pipelines.

## Consequences

### Positive

- **Bounded memory.** Peak RSS is dominated by the CsvHelper buffer (64 KB) plus the current batch of 5000 `ProductionEvent` rows (~500 KB). 200k-line files parse in bounded memory regardless of file size.
- **Per-line rejections do not abort.** `BadDataFound = null` plus the `TryMapToDomain` guard means the parser *skips* bad rows and records them. A file with 5% malformed lines still produces a `ProductionShift` with 95% of the data — the plant manager sees the shift result *and* the `LogIngestion.RowsRejected = 5000` with a sample of rejected lines.
- **Async parity.** Every layer of the ingestion (`IProductionLogParser.ParseAsync` → `ProcessLogFileCommand.Handle` → `BulkInsertAsync` → `SaveChangesAsync`) is async. No `.Result`, no `.Wait()`, no thread-pool starvation under concurrent uploads.
- **Testable with a `MemoryStream`.** A unit test constructs a small CSV string in a `MemoryStream` and asserts the `IAsyncEnumerable<ProductionEvent>` output. No file system, no mocks.
- **Locale policy is explicit.** `CultureInfo.InvariantCulture` is a single, reviewable line. The rejected line's `ErrorCode = "INVALID_DECIMAL_LOCALE"` makes the failure mode legible.

### Negative / Trade-offs

- **Two model types.** `ProductionEventRow` (flat DTO mirroring the CSV) and `ProductionEvent` (domain entity, with `SourceLineNumber`). The mapping function is where the domain invariants are applied (e.g., `quantity >= 0`). This is a small, one-file cost, but it must be documented in `Infrastructure/Parsing/README` (or a code comment) so a new contributor does not merge the two types.
- **No parallelism in the parser.** CPU-bound parse is single-threaded per ingestion. If two shifts are uploaded concurrently, they share threadpool threads via `Task.Run` around the `IAsyncEnumerable`. The domain has no shared mutable state (the parser is a pure function of the stream), so this is safe.
- **`CsvHelper` version pinning.** The version is pinned; the library is well-maintained but a breaking change between major versions is possible. Mitigated by the `IProductionLogParser` port — replacing CsvHelper with `Sep` is a one-class change.
- **Line-number tracking uses `reader.LineNumber`, which reflects physical lines, not logical CSV records.** A quoted field with embedded newlines would make `LineNumber` diverge from the logical record count. Acceptable for industrial logs (which do not contain multi-line quoted fields in the baseline); documented.
- **Rejected-rows sample is capped at 100.** A file with 100k rejected lines is truncated in the `LogIngestion.RejectedSample` JSONB. The total count `RowsRejected` is exact; the sample is a window.

## References

- KB §9.2 (structure of a parsing layer, adapted for C#), §9.3 (bulk insertion), §21.1 (DB constraints that support the parse — the `CHECK` on `ProductionEvent` fields).
- `docs/sad/01-problem-statement.md` §2.1, §2.2, §3, §4 (T1/T6).
- `docs/sad/02-context.md` §2 (volume), §4 (performance driver).
- `docs/domain/domain-model.md` §2.3, §5 (`IProductionLogParser` port, `ProductionEvent` as append-only fact).
- `docs/glossary.md` entries: `ProductionEvent`, `IProductionLogParser`, `CsvHelper`, `LogIngestion`.
- `src/OEESentinel.Infrastructure/` (parser implementation to be added under ROADMAP Phase 0, P0-4).