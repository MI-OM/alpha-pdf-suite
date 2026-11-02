# ADR-0003: Engine process isolation and IPC

**Status:** Proposed (2026-11-01)
**Decides:** where untrusted documents are parsed, how the app talks to that process, and what limits apply to it.

## 1. Context and constraints
- **The PRD requires a separate worker process** for PDFium and any other parser of untrusted documents, with a single-threaded request queue (§8). Its reasons:
  - PDFium's API is not thread-safe.
  - The prebuilt binaries lack PartitionAlloc hardening.
  - About two dozen High-severity PDFium CVEs were published in 2026.
- **OS-level sandboxing:** apply it where available, and verify it. Never describe an in-process parser as sandboxed.
- **What the worker must serve:**
  - smooth tiled rendering, so latency matters (S1)
  - printing (S3)
  - qpdf operations and redaction, whose native code can end the process (findings 28 and 37)

## 2. Options considered
- **In-process, with a global lock.** Rejected by the PRD: a crash or exploit takes down, or runs in, the app.
- **A worker process with a length-prefixed binary protocol over its stdin and stdout.** No sockets or names to secure, and the worker exits when the app does.
- **A worker over named pipes or local sockets.** More setup and naming, and a surface other local processes could reach.
- **gRPC or JSON-RPC.** More dependencies; text encoding is a poor fit for tile bitmaps.
- **Shared memory for tile pixels,** on top of any of these. Faster for large tiles, but more complex and OS-specific.

## 3. Evidence collected
- **S1 built and measured the stdin/stdout design on Windows** (spike results, S1):
  - One engine thread makes every PDFium call, in arrival order.
  - A reader thread queues requests and drops cancelled ones that have not started.
  - Tiles are capped at 4096 × 4096 px.
  - Errors carry codes, never document content.
  - **Latency:** p50 2.3 ms and p95 7.3 ms from tile request to drawn tile, while scrolling.
  - **Crash recovery:** killing the worker mid-scroll was recovered in 200 ms, with no blank frames.
- **Failure handling:**
  - A 15 s per-request watchdog kills a hung worker.
  - The session restarts a crashed worker and reopens the document. After 3 restarts within 30 s it stops (*Failed*).
  - Tests cover the kill, restart, give-up and cancellation paths on all three OSes in CI.
- **Limits:**
  - **Windows:** a Job Object caps committed memory (1.5 GB by default), kills the worker when the app's handle closes, and suppresses Windows Error Reporting. The memory cap is verified by a test.
  - **Linux and macOS:** no limit is applied yet (finding 12).
- **Defects found and fixed during the spike:** two robustness bugs (finding 23).
- **Open defect:** a cancelled open can leave a document open in the worker until it exits (finding 13).
- **Printing** needs the worker to reach the print spooler (finding 25).
- **qpdf** throws C++ exceptions across the C API in some calls, which ends the process on Linux and macOS (finding 28).
- **Redaction** still runs in-process in the spike (finding 37).
- **S6:** the packaged NativeAOT worker is 1.8 MB. A new engine-info request proved that the installed worker loads qpdf on all three OSes.

## 4. Decision and rationale
**Proposed:**
1. **Every parse of an untrusted document happens in a worker process,** never in the app. That covers rendering, text, printing, qpdf operations, redaction and its verifier.
2. **One worker per open document** (as now), started by the app and connected only through its stdin and stdout:
   - Length-prefixed binary frames, defined in `PdfSuite.Pdf.Remote`.
   - stdout carries frames only; diagnostics go to stderr.
   - Responses carry error codes, never document text.
3. **One engine thread per worker.** Requests can be cancelled before they start. A per-request watchdog applies, with longer allowances for long operations such as printing.
4. **Crash policy:** restart and reopen, up to 3 times in 30 s, then a *Failed* state the user can see.
5. **Resource limits on every OS:**
   - **Windows:** the Job Object as built.
   - **Linux and macOS:** limits to be designed and verified before those platforms are declared supported. On Linux, likely cgroups, because `RLIMIT_AS` is expected to conflict with the .NET runtime's address-space reservations; that is not tested.
6. **An OS sandbox** for the worker on each OS is a Milestone 1 investigation. It must keep access to the print spooler, or printing moves to a small broker process.

**Rationale:** the stdin/stdout design met S1's latency and recovery targets with no extra dependencies. It cannot be reached by other processes, and it already carries every operation the product needs.

## 5. Trade-offs
- **Pixels are copied through a pipe.** That is fine at S1's tile sizes; very high-DPI displays may need shared memory.
- **Memory per document:** each open document costs a process. Many open documents may need a shared worker with per-document isolation given up, or a cap on open documents.
- **The protocol is project-owned** and must be versioned with the app. The app and the worker always ship together, so there is no cross-version compatibility to keep.
- **Not a sandbox:** until OS sandboxing exists, the worker runs with the user's privileges. It limits damage from crashes and memory exhaustion, not from code execution.

## 6. Licensing and security consequences
- **Licences:** none; the protocol and client are project code.
- **What it protects against:** this is the main defence against malicious documents, crashes and memory exhaustion. The UI must never call the worker "sandboxed" until a sandbox is applied and verified on that OS (PRD §8).
- **No document text in logs:** the worker's errors name exception types only (finding 23).

## 7. Reversibility and migration cost
- **Where the transport lives:** only in `PdfSuite.Pdf.Remote`, about 1,170 lines.
- **Changing it** to sockets, or adding shared memory for tiles, is local to that project and the worker host.
- **Moving back in-process** would contradict the PRD and is not considered.

## 8. Validation experiment
- **Done in the spike:** S1's measurements and failure tests.
- **Not done yet:**
  - memory and CPU limits on Linux and macOS
  - an OS sandbox experiment on each OS: AppContainer or a restricted token on Windows, the App Sandbox or `sandbox-exec` profiles on macOS, seccomp or Landlock on Linux
  - fuzzing the worker through the protocol
  - redaction and qpdf requests moved into the worker, with tests
  - closing late-arriving opens (finding 13)

## 9. Conditions that would cause reconsideration
- **Tile throughput** becomes the bottleneck on high-DPI displays.
- **Per-document workers** use too much memory with many documents open.
- **An OS sandbox** cannot coexist with stdin/stdout or with printing, and forces a broker design.
- **Process startup** dominates for very short operations, such as batch metadata edits in a future CLI.
