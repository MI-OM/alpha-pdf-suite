# Spike Results

Results of the technical spike in `docs/06-SPIKE-PLAN.md`. Each item records its outcome, measurements, and findings. Measurements are from the development machine (Intel i5-1235U, about 8 GB RAM, Windows 11 x64) unless stated otherwise.

| Item | Status | Outcome |
|---|---|---|
| S0 Skeleton, CI, fixtures | Done | **Pass.** CI is green on Windows, macOS (arm64), and Linux (first at `2d40760`, again at `7da2c2a`). Fixtures are byte-identical across OSes except where PDFium renders text: see findings 8 and 15 |
| S1 Tiled rendering | Windows done; Linux and macOS not measured | **Pass on Windows** against every target. The pipeline tests pass on all three OSes in CI (`7da2c2a`). Interactive measurements on Linux and macOS, and worker memory limits there, are still open |
| S2 Document-text accessibility | Built; verified on Windows through UI Automation | **Promising.** The tree and keyboard reading behave as designed, with one focus event per key, and there is no measurable frame-pacing cost (A/B benchmark). Real screen-reader checks (NVDA, Narrator) are pending with the maintainer; Orca and VoiceOver need Linux and a Mac |
| S3 Printing | Not started | — |
| S4 qpdf from .NET | Not started | — |
| S5 Redaction method | Not started | — |
| S6 Packaging | Workflow scaffolded | Not started. A local win-x64 self-contained publish works: 211 MB, of which about 100 MB is SkiaSharp and HarfBuzz native `.pdb` symbols. The published worker self-test passes |

## S0 — Skeleton, CI, and fixtures (2026-10-21)

### Built
- `.NET 10.0.402` SDK, Avalonia 12.1.4, PDFium chromium/8111 (bblanchon 158.0.8111), xunit.v3 4.0.2 on Microsoft.Testing.Platform.
- Projects:
  - `PdfSuite.Pdf.Abstractions`
  - `PdfSuite.Pdf.Pdfium`: project-owned `LibraryImport` bindings, a thread-safe read adapter, and an authoring API.
  - `PdfSuite.Pdf.Worker`: a self-test host.
  - `PdfSuite.App`: an empty Avalonia shell.
  - `tools/PdfSuite.FixtureGenerator`
  - `tests/PdfSuite.Spike.Tests`
- Central package management, lock files, NuGet audit, warnings as errors.
- CI matrix workflow and packaging scaffold (`.github/workflows/`). These have not run yet, because the repository has not been pushed.
- `PdfSuite.Pdf.Qpdf` is not created yet. It has no code until S4.

### Evidence
- The CI steps were run locally from a clean checkout in this order, and all passed: locked restore, Release build, tests, fixtures, worker self-test.
- `dotnet build` succeeded with 0 warnings and 0 errors.
- `dotnet test` passed 15 of 15 tests in about 25 s.
- The fixture generator produces 8 fixtures and `manifest.json`. Two separate runs produced byte-identical files (SHA-256 compared).
- The worker self-test opened the 2,000-page fixture in its own process and printed `{"pageCount":2000,"firstPage":{"width":612,"height":792}}`. Its exit codes are 0 for success, 1 for an unreadable document, and 2 for a usage error.
- The App window opened. Time to window for a Release build, JIT, framework-dependent:
  - first launch: 11.7 s (probably Defender's first-run scan)
  - warm launches: 3.5 s and 2.9 s

  The PRD target is under 3 s. S6 measures NativeAOT and ReadyToRun builds.

### Findings
1. **`FPDFPage_GenerateContent` takes time proportional to the document's page count**, so writing content to every page of an N-page document is quadratic.

   | Pages appended (one rectangle each) | Time |
   |---|---|
   | 200 | 0.87 s |
   | 400 | 3.1 s |
   | 800 | 14.6 s |

   - Empty pages are not affected.
   - Inserting at the front does not help.
   - **Workaround:** build 100-page chunk documents and append them with `FPDF_ImportPages`. This gave 2,000 pages in 1.6 s in the probe, and cut the fixture generator from 94 s to about 10 s.
   - **Product impact:** whole-document operations that edit every page (OCR text layers, flattening, rasterising redaction, batch stamping) must use chunking or a similar approach. Single interactive edits cost about 50 ms each at 2,000 pages, which is acceptable.
   - Track this as an input to S5 and the OCR milestone.
2. **PDFium output is not reproducible as written.**
   - Every save gets a random `/ID`.
   - Every new document and embedded file gets the current `/CreationDate`.
   - PDFium has no API to set either. The generator overwrites both in place (they have a fixed length, so cross-reference offsets stay valid), and the fixtures are then byte-reproducible.
   - The app must not do this to user documents. It only matters for test fixtures.
3. **Telemetry in dependencies.** Avalonia.BuildServices sends build telemetry, and the test platform sends test telemetry. Both are now disabled at repository level and verified (`docs/licensing/dependency-inventory.md` §3). Earlier local builds probably sent Avalonia's anonymous build data.
4. **The PDFium NuGet packages ship no licence or notice files.** Notices must come from the matching release archive. This is a release blocker, tracked in the inventory.
5. **xunit.v3 4.x needs the Microsoft.Testing.Platform runner** (`global.json` → `test.runner`). The VSTest adapters are no longer needed.
6. **A local `dotnet publish -r` rewrites the committed lock files.**
   - It adds a RID section to the lock files, and CI's RID-less locked restore then fails with `NU1004`. That is what happened on the first CI run (2026-10-22, `d01bf8d`), on all three OSes: a `win-x64` publish made for the S6 check was committed.
   - Fixed by regenerating the lock files.
   - `Directory.Build.props` now sends RID restores to `obj/packages.<rid>.lock.json`. Verified by repeating the publish: the committed lock files were unchanged.
7. **The v4 GitHub Actions run on the deprecated Node 20.** They were moved to checkout v7, setup-dotnet v6, upload-artifact v7, and download-artifact v8 (all Node 24), with `persist-credentials: false`.
8. **PDFium output is not byte-identical across operating systems.**
   - At `2d40760`, every OS passed every step: locked restore, build, 15 tests, fixture generation, and the worker self-test. Job times: Windows 85 s, macOS arm64 52 s, Linux 34 s.
   - The informational comparison of fixture hashes failed, so at least one fixture differs between Linux and Windows or macOS.
   - The job log and hash artifacts need a GitHub sign-in to read. CI now also publishes each OS's hashes as public annotations, so the next run shows which fixtures differ.
   - **Explained in finding 15:** only the fixture rendered with PDFium differs, plus the manifest's line endings.

### Not yet covered by S0
- **Password-protected, metadata, JavaScript, bookmark, hidden-layer, and vector-art canary fixtures.** They need qpdf or raw object writing, and arrive with S4 and S5.

## S1 — Out-of-process tiled rendering (2026-10-31)

### Built
- **`PdfSuite.Pdf.Remote`** (new project):
  - A length-prefixed binary protocol over the worker's stdin and stdout.
  - `WorkerClient`, with a watchdog that kills a worker whose request takes longer than 15 s.
  - `DocumentSession`, which restarts a crashed worker and reopens the document. After 3 restarts within 30 s it gives up and enters a *Failed* state.
  - On Windows, a Job Object: committed-memory cap (default 1.5 GB), the worker killed when the app's handle closes (including when the app crashes), and no Windows Error Reporting dialog if the worker crashes.
- **Worker `--serve` mode:**
  - One thread makes every PDFium call, in arrival order.
  - A reader thread queues requests and drops cancelled ones that have not started.
  - It keeps 8 pages loaded and exits when stdin closes.
  - Tiles are capped at 4096 × 4096 px.
  - Errors carry codes, never document content.
- **`IPdfDocument.RenderRegion` and `PageSize.ToPixels`:** the worker and the view use the same pixel rounding, so the tile grid always matches what the engine draws.
- **App:**
  - `DocumentView` with a tile cache: least recently used tiles are dropped first, under a 384 MB budget, and tiles drawn in the current frame are never evicted.
  - Low-resolution page previews at 0.3 px/pt, drawn under missing tiles.
  - Requests in priority order: previews of visible pages, then visible tiles, then one viewport above and below. Requests for tiles that leave that range are cancelled, with at most 24 in flight.
  - **Open PDF…** through the native file dialog, a status bar, and error messages from the UI copy rules.
  - `--benchmark`, `--software`, `--tile`, and `--kill-worker` options.
- **10 new tests on a real worker process:**
  - page sizes, and the open error for invalid data
  - text drawn exactly where the page has it
  - tiles matching the full page
  - cancellation
  - worker kill, session restart, and giving up after repeated crashes
  - the Windows memory limit
  - the worker ending when the client is disposed

  The total is 25, all passing locally.

### Measurements (Windows)
Machine: i5-1235U, 12 logical cores, Windows 11 x64, scaling 1.0, 1000 × 701 DIP viewport, Release build, JIT. The document is `large-2000-pages.pdf`, and scrolling runs at a constant 3,000 DIP/s. Raw results are in `docs/research/s1-benchmarks/`.

| Target | GPU, 256 px tiles | GPU, 512 px tiles | Software, 256 px tiles |
|---|---|---|---|
| First page sharp after opening, < 2 s | **0.84 s** ✅ | **0.47 s** ✅ | **0.43 s** ✅ |
| Frame interval p95 ≤ 33 ms (100% / 25% / 400% / 800%) | 17.4 / 17.3 / 17.4 / 17.3 ms ✅ | 17.3 / 17.3 / 17.2 / 17.3 ms ✅ | 31.2 / 31.4 / 31.5 / 31.3 ms ✅ (barely) |
| No UI stall > 100 ms (worst frame) | 51 ms ✅ | 34 ms ✅ | 35 ms ✅ |
| App + worker < 1 GB (peak working set) | 641 MB ✅ | 636 MB ✅ | 468 MB ✅ |
| Frames with blank tiles while scrolling | 0% at 100%, 400%, 800% | 0.2% at 25% only | 0% |
| Killing the worker mid-scroll | App keeps running; recovered in **200 ms**; no blank frames | not run | not run |
| Tile latency p50 / p95 (request to drawn) | 2.3 / 7.3 ms | 3.2 / 8.5 ms | 1.9 / 8.0 ms |
| Worker render time p50 / p95 | 0.07 / 2.3 ms | 0.36 / 2.9 ms | 0.07 / 2.2 ms |

- **Drawing cost:** the UI thread spends under 0.2 ms per frame drawing (p95), so frame pacing is set by the compositor, not by the view.
- **Visual check:** a window capture shows correct pages, text, and images, with no seams.
- **Startup:** process start to window took 5.5 s on a cold first launch of a fresh Release build, against 2.9–3.5 s warm in S0. This is S6's problem: NativeAOT and ReadyToRun.
- **Re-measured after S2 (finding 19):** six further runs with the GPU and 256 px tiles met every S1 target. The worst frame was 36 ms and p95 17.6–18.5 ms at all zoom levels.

### Findings
9. **Tiles differ from a full-page render only where a glyph crosses a tile edge.**
   - PDFium snaps each glyph to whole pixels from the bitmap's origin, so a straddling glyph can be anti-aliased differently in two adjacent tiles.
   - At 1.5×: 0.25% of the page's pixels differ, all within 12 px of a tile edge, with per-channel deltas up to about 130.
   - A magnified side-by-side comparison shows no visible seam: a straddling stroke is marginally heavier.
   - The test now asserts identical pixels away from tile edges, and fewer than 1% differing in total.
10. **Software rendering is just within budget.** p95 31.2–31.5 ms against 33 ms: frames alternate between 16.7 ms and 33 ms. Machines and VMs without GPU acceleration will be close to the limit. This needs measuring on Linux (S1) and in remote-desktop sessions.
11. **The tile cache probably dominates app memory.** The app's working set is about 600 MB with GPU rendering, against 468 MB combined with software rendering. That fits the 384 MB tile budget plus GPU-side copies of the tiles, but this is inferred, not profiled. A smaller budget is an easy lever if memory matters more than revisiting pages.
12. **Worker memory limits exist on Windows only.** The Job Object cap is verified by a test: a 512 MB allocation is refused under a 256 MB cap, and the worker keeps serving. No limit is applied yet on Linux or macOS. `RLIMIT_AS` is expected to conflict with the .NET runtime's large address-space reservations (not yet tested), so cgroups on Linux are the likely route; macOS is to be investigated.
13. **A cancelled open can leave a document open in the worker until it exits.** This is acceptable for the spike; it needs a close-on-late-arrival rule before Milestone 1.
14. **A .NET patch release broke CI with no code change.**
    - On 2026-10-25, .NET 10.0.14 and SDK 10.0.403 were released.
    - With `global.json` set to `rollForward: latestFeature`, `actions/setup-dotnet` installs the newest 10.0 SDK.
    - That SDK implicitly references `Microsoft.NET.ILLink.Tasks` 10.0.14, while the lock files recorded 10.0.13. The locked restore then failed on all three OSes (run 41165944204, `59ef911`).
    - **Fix:** the SDK is pinned exactly (`rollForward: disable`). Moving to a new SDK is now a deliberate commit that updates `global.json` and the lock files together (README).
    - **Consequence:** .NET security patches arrive only when someone bumps the pin. That needs a routine, monthly after Patch Tuesday, because self-contained builds ship the pinned runtime.
15. **PDFium's writing is identical across operating systems; its rendering is not.**
    - CI at `7da2c2a` (run 41166246083) was green on all three OSes, with all 25 tests passing, including the 10 worker-pipeline tests on macOS and Linux.
    - The published hashes showed that 7 of the 8 fixture PDFs are byte-identical on Windows, macOS, and Linux.
    - The exception was `canary-image.pdf`, the only fixture made by *rendering* text into pixels. Its hash differed on all three OSes.
    - The cause is not yet investigated. The likely explanation is that non-embedded standard fonts (Helvetica) are substituted or rasterised differently on each OS.
    - `manifest.json` differed on Windows only, because of .NET's OS-dependent JSON line ending.
    - **Fixes:** the canary label is now drawn with a built-in 5×7 pixel font instead of PDFium rendering, and the manifest is written with `\n`. **Confirmed by CI at `e0e27b0` (run 41167292876): every fixture and the manifest are byte-identical on Windows, macOS, and Linux.**
    - **Consequence for testing:** tests that compare rendered pixels need a baseline per OS (or tolerances). Tests that compare written PDFs can compare bytes across OSes.
    - **Consequence for the product:** the same document can look slightly different on each OS when its fonts are not embedded. This is normal for PDF viewers, but it should be documented, and it matters for the visual-regression corpus.

### Still open for S1
- **Linux (X11 and XWayland) and macOS:** the interactive benchmark and a visual check. CI runs the pipeline tests on all three OSes.
- **Worker memory limits on Linux and macOS.**
- **Display scaling above 1.0:** tile size in device pixels is implemented, but not yet measured.

## S2 — Document-text accessibility (2026-10-31)

### Approach
Avalonia exposes no text pattern to assistive technology. Issue #22168 is still open with no comments. Upstream PR #22178, which only defines the shared text contract (+1,428 lines), is open with no reviews and labelled *api-needs-review*. The Windows TextPattern and AT-SPI text handlers would follow in later PRs, and nothing is listed for macOS.

So S2 does not wait for that work. It uses the one channel every screen reader on every OS supports, **keyboard focus**:
- **Engine:** `IPdfDocument.GetTextLines` returns each line's text and bounds. The worker serves it (`GetTextLines` request).
- **View:** `DocumentView` is exposed as a *Document* named after the file. It hosts a *Group* per page ("Page N of M") for the visible pages and the page being read, and inside each group a focusable *Text* element per line, drawn with a highlight while focused.
- **Pages without text:** the page group itself takes focus and is named "Page N of M, no text on this page", or "…, the text on this page could not be read" if extraction failed.
- **Keys:**
  - ↓/↑: next or previous line, across pages
  - PageDown/PageUp: next or previous page
  - Ctrl+Home/End: first or last page
  - Space: scroll

  The view scrolls to keep the focused line in sight. Tab enters the document once (`TabNavigation = Once`) and returns to the last reading position.

### Evidence (Windows, UI Automation)
`tools/spike/s2-uia-check.ps1` starts the app on `image-only-middle-page.pdf`, prints the UI Automation tree, presses keys, and logs the focused element and the UIA focus-changed events. Those events are what NVDA and Narrator announce.

```
Document: Document | image-only-middle-page.pdf
  Group: Page 1 of 3  (6 text lines)
      Text | Text page 1
      Text | document update report index policy account archive record s...
Down       -> Text | Text page 1                              (1 focus event)
Down       -> Text | document update report index policy...   (1 focus event)
Down       -> Text | section document update report index...  (1 focus event)
PageDown   -> Group | Page 2 of 3, no text on this page        (1 focus event)
Down       -> Text | Text page 2                              (1 focus event)
Up         -> Group | Page 2 of 3, no text on this page        (1 focus event)
Up         -> Text | analysis review table section...         (1 focus event)
Ctrl+End   -> Text | Text page 2                              (1 focus event)
Ctrl+Home  -> Text | Text page 1                              (1 focus event)
```

Against the S2 questions:

| Question | Windows (UIA) | Real screen reader |
|---|---|---|
| (a) Can the user hear the page text? | ✅ Each line is a named Text element and takes focus | NVDA and Narrator pending (checklist below) |
| (b) Can the user move line by line? | ✅ ↓/↑, one focus event per line, across page boundaries | Pending |
| (c) Can the user move to the next page? | ✅ PageDown; a text-less page is announced as such | Pending |
| (d) Can the user find their position after scrolling? | Focus stays on the reading line during mouse scrolling; the next ↓ continues from it and scrolls back to it. Each line sits in a group named "Page N of M" | Pending: does the reader announce the page group? |

Orca (Linux) and VoiceOver (macOS) are untested. They need a Linux environment and a Mac.

### Findings
16. **PDFium returns text lines in content-stream order, not visual reading order.**
    - In `canary-text.pdf`, the "CANARY-01" line is drawn above the filler but written after it, so it comes last. A test pins this behaviour.
    - For untagged PDFs, what a screen-reader user hears therefore depends on how the file was produced.
    - Sorting lines top-to-bottom would break multi-column pages, so the spike keeps the engine's order.
    - **v1 needs a reading-order decision:** use the structure tree for tagged PDFs, and layout analysis or a column-aware heuristic for untagged ones. PdfPig (Apache-2.0) has layout analysis and is a candidate.
17. **Keyboard focus is a viable route to document text without a text pattern.** It is cross-platform in principle, because it relies only on focus events. It has known limits, which the manual checks will size:
    - no continuous "say all" (each line is its own focus stop)
    - no character or word navigation through the document, beyond what a reader's review cursor offers on the focused line
    - no keyboard text selection
    - possible extra verbosity from announcing roles
    - These are what a real text pattern would add later. They do not block S2's pass criteria.
18. **Arrow keys now move a visible reading highlight instead of scrolling.** For sighted keyboard users the effect is similar (the view follows the line), and Space and the mouse wheel still scroll freely. Confirm this design in UI review.
19. **An S2 change caused 100–250 ms stalls; found by A/B benchmark and fixed.**
    - The first S2 build invalidated the view's layout on every scroll step and every tile arrival, even when there were no page elements to move.
    - In a first A/B round (4 runs with the reading elements and 4 without), 5 of 8 runs had a stall over 100 ms, the worst at 254 ms, in both variants. The view also ran up to about 900 extra render passes per second.
    - **Fix:** invalidate layout only when page elements are added or removed, or when existing ones need to move.
    - A second round (3 + 3 runs, `docs/research/s2-benchmarks/`) had **no frame over 50 ms in any of 24 phase runs** (worst 36 ms) and **no extra render passes**.
    - p95 was 17.6–18.5 ms in every phase, with or without the reading elements. **The reading elements have no measurable frame-pacing cost.** They add about 10 MB of allocation per 15 s scroll phase.
    - Garbage collection runs 1–6 full collections per phase in both variants (likely bitmap memory pressure), with 3–37 ms of total pause and no stalls.
20. **Avalonia's animation-frame callback also runs on render passes caused by invalidation**, not only once per display frame. The benchmark now counts callbacks under 2 ms apart as extra ticks rather than frames. Without that, the earlier statistics were diluted, for example a 0.14 ms median "frame" in one run.

### Manual screen-reader checklist (Windows)
Prerequisites: install NVDA (free, from nvaccess.org), and build the app (`dotnet build PdfSuite.slnx`). Then open the test document:

```bash
dotnet run --project src/PdfSuite.App -- fixtures/generated/image-only-middle-page.pdf
```

With NVDA running, then again with Narrator (Win + Ctrl + Enter):

1. Press **Tab** once from **Open PDF…**. Expected: the document name is announced.
2. Press **↓**. Expected: "Text page 1" is announced. This tests (a).
3. Press **↓** twice. Expected: the next two lines are read. This tests (b).
4. Press **PageDown**. Expected: "Page 2 of 3, no text on this page" is announced. This tests (c).
5. Press **↓**. Expected: "Text page 2" is announced. Note whether "Page 3 of 3" is announced as you enter the page. This tests (d).
6. Scroll away with the mouse wheel, then press **↓**. Expected: reading continues from the last line, and the view scrolls back to it. This tests (d).
7. Note anything distracting: repeated role words ("text", "grouping"), lag, or anything not read.

Pass: steps 2–5 work in both readers. Record what was heard for each step here.
