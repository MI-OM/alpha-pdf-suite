# Spike Results

Results of the technical spike in `docs/06-SPIKE-PLAN.md`. Each item records its outcome, measurements, and findings. Measurements are from the development machine (Intel i5-1235U, about 8 GB RAM, Windows 11 x64) unless stated otherwise.

| Item | Status | Outcome |
|---|---|---|
| S0 Skeleton, CI, fixtures | Done | **Pass.** CI is green on Windows, macOS (arm64), and Linux (first at `2d40760`, again at `7da2c2a`). Fixtures are byte-identical across OSes except where PDFium renders text: see findings 8 and 15 |
| S1 Tiled rendering | Windows done; Linux and macOS not measured | **Pass on Windows** against every target. The pipeline tests pass on all three OSes in CI (`7da2c2a`). Interactive measurements on Linux and macOS, and worker memory limits there, are still open |
| S2 Document-text accessibility | Built; verified on Windows through UI Automation | **Promising.** The tree and keyboard reading behave as designed, with one focus event per key, and there is no measurable frame-pacing cost (A/B benchmark). Real screen-reader checks (NVDA, Narrator) are pending with the maintainer; Orca and VoiceOver need Linux and a Mac |
| S3 Printing | Windows done; macOS and Linux planned and estimated | **Pass on Windows.** The worker prints with exact page geometry and vector output, but slowly: about 1 s and 256 KB per page with Print to PDF. macOS and Linux have a researched plan and estimates, not yet a proof of concept |
| S4 qpdf from .NET | Done | **Pass on all three OSes** (CI at `62a2efe`, run 41327508609): AES-256 encryption, decryption, Unicode metadata, and full rewrites, all checked with PDFium as an independent reader |
| S5 Redaction method | Done; runs in the worker since 2026-11-06; ADR-0006 proposed | **Pass on all three OSes** (CI at `29ed037`, run 41338226605). No canary survives in any of the 7 canary fixtures (13 canaries), and all 7 negative controls are rejected, each by the check meant for it. Glyph-level probe: **yes** for the three fonts tried, with caveats for real-world fonts (finding 36). Still to do: run it in the worker, and write ADR-0006 |
| S6 Packaging | Done; ADR-0005 proposed | **Pass on all three OSes** (Package workflow at `81ab077`, run 41356497705). The MSIX, DMG, AppImage and .deb each installed on a fresh runner and launched the S1 viewer, which rendered the 2,000-page fixture and loaded qpdf in the worker. Packages are NativeAOT and 18–24 MB. Over two runs, a first launch after installing reached the window in 0.07–0.62 s and the first page in 0.15–2.4 s; the AppImage took 1.7–3.6 s to the window. Not yet distributable: the MSIX has a test certificate, and Gatekeeper rejects the DMG, which is not notarised |

## Go/no-go (2026-11-01)
The spike ran from 2026-10-21 to 2026-11-01. This section applies the rules in spike plan §6 to each outcome. The ADRs in `docs/adr/` turn the outcomes into proposed decisions, which a maintainer accepts or changes.

| Item | Outcome | Rule (spike plan §6) | Result |
|---|---|---|---|
| S1 Tiled rendering | Pass on Windows. The pipeline tests pass on all three OSes, and the packaged builds render on all three. Interactive measurements on Linux and macOS are not done | S1 and S2 pass: commit to Avalonia | Rendering is not a blocker. The commitment to Avalonia waits on S2 |
| S2 Document text | Built, and verified through UI Automation on Windows. Real screen-reader checks are not done | As above. Windows only: the maintainer chooses. Fails on Windows: Qt 6 comparison | **Open.** The NVDA and Narrator checks decide it |
| S3 Printing | Pass on Windows. Estimated 2–3 days for Linux and 2–5 days for macOS | Too costly on macOS or Linux: use the OS default handler there | Not too costly on these estimates. Build the native routes in Milestone 1, with the default handler as the fallback (ADR-0001) |
| S4 qpdf from .NET | Pass on all three OSes | Fails: compare a PDFBox sidecar with an in-house writer | qpdf is the structural engine (ADR-0002) |
| S5 Redaction method | Pass on all three OSes; every negative control rejected | Fails: redaction moves to v2 | The rebuild method is v1's redaction (ADR-0006) |
| S6 Packaging | Pass on all three OSes | Fails on an OS: not supported there | Packaging blocks no platform (ADR-0005) |

**Overall:**
- **Ready for Milestone 1:** the engine, the worker, redaction and packaging.
- **Not yet ready:** Avalonia, the one framework choice the spike was run to test. That waits only on S2: until NVDA and Narrator read document text line by line, ADR-0001 should not be accepted.
- **No platform is declared supported yet.** PRD §6 also requires accessibility testing and native smoke tests, which are open for macOS and Linux.

**Before Milestone 1** (the roadmap's Milestone 0 gate):
- Run the S2 checklist with NVDA and Narrator (maintainer).
- Accept, change or reject ADR-0001 to ADR-0006.
- Decide the open questions the ADRs depend on (spike plan §9):
  - the project licence (ADR-0004)
  - the Apple Developer Program, and the Windows signing route (ADR-0005)
  - the product name

**Carried into Milestone 1 as work:**
- redaction and qpdf operations in the worker (finding 37): **done 2026-11-06**, see "After the spike"
- worker limits on Linux and macOS (finding 12)
- reading order (finding 16), and hiding hidden-layer text from screen readers (finding 32): **both done 2026-11-06** for what the document declares ("After the spike"). Reading order for untagged pages still needs a decision
- proofs of concept for macOS and Linux printing
- Orca and VoiceOver checks, and the S1 measurements on Linux and macOS
- locked publish restores (finding 42)

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
- **Startup:** process start to window took 5.5 s on a cold first launch of a fresh Release build, against 2.9–3.5 s warm in S0. This is S6's problem: NativeAOT and ReadyToRun. S6 measured both (finding 39).
- **Re-measured after S2 (finding 19):** six further runs with the GPU and 256 px tiles met every S1 target. The worst frame was 36 ms and p95 17.6–18.5 ms at all zoom levels.

### Findings
9. **Tiles differ from a full-page render only where a glyph crosses a tile edge.**
   - PDFium snaps each glyph to whole pixels from the bitmap's origin, so a straddling glyph can be anti-aliased differently in two adjacent tiles.
   - At 1.5×: 0.25% of the page's pixels differ, all within 12 px of a tile edge, with per-channel deltas up to about 130.
   - A magnified side-by-side comparison shows no visible seam: a straddling stroke is marginally heavier.
   - The test now asserts identical pixels away from tile edges, and fewer than 1% differing in total.
10. **Software rendering is just within budget.** p95 31.2–31.5 ms against 33 ms: frames alternate between 16.7 ms and 33 ms. Machines and VMs without GPU acceleration will be close to the limit. This needs measuring on Linux (S1) and in remote-desktop sessions.
11. **The tile cache probably dominates app memory.** The app's working set is about 600 MB with GPU rendering, against 468 MB combined with software rendering. That fits the 384 MB tile budget plus GPU-side copies of the tiles, but this is inferred, not profiled. A smaller budget is an easy lever if memory matters more than revisiting pages.
12. **Worker memory limits exist on Windows only.** (Linux and macOS limits added 2026-11-06; see "After the spike".) The Job Object cap is verified by a test: a 512 MB allocation is refused under a 256 MB cap, and the worker keeps serving. No limit is applied yet on Linux or macOS. `RLIMIT_AS` is expected to conflict with the .NET runtime's large address-space reservations (not yet tested), so cgroups on Linux are the likely route; macOS is to be investigated.
13. **A cancelled open can leave a document open in the worker until it exits.** This is acceptable for the spike; it needs a close-on-late-arrival rule before Milestone 1. **Fixed 2026-11-06:** the client closes the document when the late answer arrives.
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
- **Worker memory limits on Linux and macOS:** done 2026-11-06 and passing in CI ("After the spike").
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
    - **Tagged pages done 2026-11-06:** they are read in structure-tree order ("After the spike"). Untagged pages still follow the file, pending the layout-analysis decision.
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

## S3 — Printing (2026-10-31)

### Built (Windows)
- **Where the document is parsed:** only in the worker, as PRD §8 requires.
  - The app shows the Print dialog (`PrintDlgW`) and passes the chosen printer name, its DEVMODE settings (which carry copies and collation), and the page range to the worker.
  - The worker opens the printer (`CreateDC`) and draws each page with PDFium's Windows-only `FPDF_RenderPage`, so pages go out as GDI drawing commands, not bitmaps.
- **Placement:**
  - each page centred on the paper at actual size
  - shrunk only if larger than the printable area
  - turned a quarter if its orientation differs from the paper's
- **App:** a **Print…** button and Ctrl+P. Messages: "Sent N page(s) to <printer>.", "Printing was cancelled.", and "We couldn't print the document. Check the printer, then try again."
- **Code size:** about 300 lines of Windows interop (`GdiPrinter`, `WindowsPrintDialog`) plus about 70 lines of protocol and client code.
  - It took about half a day, including measurement and the dialog investigation.
  - The hang watchdog allows 30 s plus 10 s per page for a print request.

### Evidence
- **Worker test** (`Print_ToMicrosoftPrintToPdf_ProducesEveryPageAtTheRightSize`) prints `simple-text.pdf` to Microsoft Print to PDF with an output file. It checks three pages, 612 × 792 pt, ink where the heading is, and a blank margin. It is skipped where that printer is not installed.
  - It passes locally.
  - CI at `842f7f8` (run 41196193897) is green on all three OSes. Whether the test ran or skipped on the GitHub Windows runner is not visible without signing in to read the logs.
  - **Settled 2026-11-06:** the test summary in the Windows job at `2357e4e` shows 71 passed and only the 3 explicit probes skipped, so it runs and passes on the GitHub Windows runner.
- **Dialog check** (`tools/spike/s3-print-dialog-check.ps1`, passed):
  1. Ctrl+P opens the dialog.
  2. The script confirms "Microsoft Print to PDF" is selected (it never presses Print otherwise).
  3. Print starts a job: the driver's Save dialog appears.
  4. Cancelling the Save dialog makes the app report "Printing was cancelled."
- **Fidelity and cost:** measured by printing to Microsoft Print to PDF and comparing with the original, rendering page 1 at 2 px/pt.

| | simple-text (3 pages) | large-mixed (first 50 pages) |
|---|---|---|
| Time until the file was complete | 4.1 s | 48.1 s (about 1 s per page) |
| Output size | 683 KB (original: 3.5 KB) | 12.8 MB |
| Page 1 size | 612 × 792 pt, unchanged | unchanged |
| Page 1 content | 41 text objects → 2,259 vector paths, 0 images | 46 text → 2,541 paths; the image stays an image |
| Pixels differing by more than 25% luminance | 2.0% | 1.3% |

A side-by-side crop showed the printed text matching the original in position and shape, marginally heavier.

### Findings
21. **Windows printing works, but text is printed as glyph outlines.** Quality is vector and geometry is exact. The cost is about 1 s and about 256 KB of spool data per page with Microsoft Print to PDF, and print-to-PDF output has no extractable text.
    - PDFium can send real GDI text instead (`FPDF_SetPrintTextWithGDI`, as Chrome does), but **the prebuilt binaries do not export it**: it needs the `PDFIUM_PRINT_TEXT_WITH_GDI` build flag, checked against the DLL's export table.
    - So faster, smaller printing would need **our own PDFium build**. Flathub already requires building PDFium from source, so the two decisions should be made together.
    - Not yet measured on a physical printer. Printing real pages is your choice to make, not the spike's.
22. **On Windows 11 the classic `PrintDlgW` call shows the modern "Windows Print" dialog**, which is hosted by a system process. It says "This app doesn't support print preview".
    - Preview in that dialog needs the WinRT printing APIs (`Windows.Graphics.Printing`). This is a v1 UX decision.
    - The driver's Save dialog is parented to the app's window.
23. **Two robustness bugs found and fixed while building S3:**
    - The client's reader thread could crash the whole app if reading a dead worker's exit code threw (seen as `UnauthorizedAccessException` when it raced with disposal). That would have defeated S1's crash isolation; it is now caught.
    - The worker died on an exception type its request handler did not list (`EntryPointNotFoundException` for a missing PDFium export). Any unexpected exception now becomes an error response that names only its type, and the worker keeps serving.
24. **Cancelling a print-to-file Save dialog is a cancellation, not an error.** `StartDoc` fails with `ERROR_CANCELLED`; the worker returns 0 pages and the app says "Printing was cancelled."
25. **Printing needs the worker to reach the print spooler.** Any future OS sandbox for the worker must allow that, or printing moves to a small separate broker process. Record this in the process-isolation ADR.

### macOS and Linux: plan and estimates (researched, not built)
Same rule: the app never parses the document. Both OSes use CUPS, so one approach can cover both: **hand the original PDF to the OS print system**, which renders it in its own sandboxed filter processes.

| | Recommended route | Dialog | Estimate | Main risks |
|---|---|---|---|---|
| **Linux** | xdg-desktop-portal **Print** (`PreparePrint` shows the native dialog; `Print` takes a file descriptor of the PDF), over D-Bus with Tmds.DBus, which Avalonia already brings in. Works inside Flatpak too | The portal's (GTK or KDE) | 2–3 days | The portal is missing on minimal desktops (fall back to CUPS `lp`); CUPS filters render with Poppler or Ghostscript, so output can differ from PDFium |
| **macOS** | libcups (`cupsGetDests` and `cupsPrintFile`) with a small Avalonia print dialog. Later, `NSPrintPanel` through Objective-C interop for the native look | Own dialog first; native later (+3 days) | 2 days, or 5 with the native panel | Quartz renders the PDF, so output can differ from PDFium; Objective-C interop without a binding library is laborious |

**Fallback where none of this works:** render pages in the worker at printer resolution and send raster images to CUPS. This matches PDFium exactly, but jobs are large.

**Confidence: low to medium.** These are estimates from documentation, not measurements. Each route needs a 1-day proof of concept on a real machine (spike plan §9: Linux environment and Mac access).

## S4 — qpdf from .NET (2026-10-31)

### Built
- **`tools/qpdf/fetch-qpdf.ps1`:**
  - Downloads qpdf 12.4.2's official prebuilt archive for a runtime identifier: Windows x64 and arm64, Linux x64 and arm64, macOS arm64 and x64.
  - Checks it against a SHA-256 pinned in the script (from qpdf's published checksum list), and refuses and deletes it on a mismatch.
  - Copies the library files to `artifacts/qpdf/native/<rid>/`. A second run with the same version does nothing.
  - CI runs it before restore. A build without it fails with an instruction.
- **`PdfSuite.Pdf.Qpdf`:**
  - project-owned `LibraryImport` bindings to qpdf's C API
  - a resolver that loads the versioned library file by path, and on Linux first loads the libraries qpdf's archive bundles
  - `QpdfStructure`: AES-256 encryption (PDF 2.0, revision 6) with permissions; decryption; document information read and write through qpdf's object API; full rewrites, optionally "inspectable" (decoded streams, no object streams)
  - `PdfText.Contains`: searches raw bytes for text as a literal, as hex in either case, and as UTF-16BE
- **`PdfiumDocument.GetMetadata`,** so qpdf's output is checked by an independent reader.

### Evidence
CI at `62a2efe` (run 41327508609) passed on Windows x64, Linux x64, and macOS arm64. On every runner the pinned qpdf archive downloaded and verified (1–3 s), the native library loaded (on Linux, after preloading its bundled libraries), and all 39 tests passed. 8 of them are new:
- **Encryption:** AES-256 output has `/V 5 /R 6`. PDFium refuses it with no password and with a wrong one, and opens it with either the user or the owner password.
- **Decryption:** with the right password, the output opens unencrypted. With a wrong one, qpdf reports a password error (code 4).
- **Metadata:** "Résumé – naïve test ✓" round-trips through qpdf, and PDFium reads it identically, also after encryption. A null value removes a key.
- **Full rewrite:** keeps only the current revision, so the earlier revision's canary is gone. A negative control proves the search does find that canary in the first revision on its own.
- **Determinism:** unencrypted rewrites are byte-identical.
- **Invalid input:** raises `QpdfException`.

### Findings
26. **qpdf works from .NET with pinned, verified official binaries for all six targets.** No NuGet package was needed: the only one, `QPdf.RuntimeLibraries`, is a community package stuck on 11.9.1.
    - The Linux archive bundles **LGPL libraries** (GnuTLS and its dependencies), which brings distribution obligations; Windows and macOS do not.
    - Building qpdf with only its own crypto provider would remove them. Decide that together with building PDFium ourselves (findings 21 and Flathub).
27. **qpdf's `qpdf_set_info_key` stores the bytes it is given, not text.** A UTF-8 title came back intact through qpdf, but was stored as raw bytes (`<52c3a973…>`) that every other reader would show as "RÃ©sumÃ©". Metadata now goes through qpdf's object API (`qpdf_oh_new_unicode_string`), and the test checks with PDFium.
28. **Some of qpdf's C functions throw C++ exceptions instead of returning an error.**
    - Setting encryption while deterministic IDs are on raised `SEHException` on Windows. On Linux and macOS a C++ exception crossing into .NET aborts the process.
    - The code avoids that combination, and the bindings document the hazard.
    - **This is another reason qpdf must run only in the worker,** where a crash is contained.
29. **PDFium writes text as hex strings in `TJ` arrays,** for example `[<43414E4152592D3031>] TJ`. A literal byte search for a canary therefore never matches, and a leak check built on one passes vacuously.
    - `PdfText.Contains` searches all common forms, and every leak test now needs a negative control that proves the search can fail.
    - Font encodings that remap codes are still not covered; S5's verifier adds text extraction for those.

## S5 — Redaction method and verifier (2026-11-01)

### Built
- **`PdfSuite.Pdf.Redaction`:**
  - **`Redactor`** builds a new document and never edits the source:
    - Each page with an area is rendered at 216 dpi, annotations included, and the areas are filled black, rounding outwards. The page becomes a single image.
    - Every other page is copied unchanged, in runs.
    - qpdf then applies the scrub policy and writes the file once, as a single revision:
      - the catalog keeps only `/Type` and `/Pages`
      - `/Info` and the file ID are removed
      - page actions, page metadata, private application data and beads are removed from every page
      - annotation actions, page links, reply links, and links that lead outside the output are removed
    - The result goes through the verifier, and a file that fails is never returned.
  - **`RedactionVerifier`** is public and does not trust how the file was made. It runs 7 checks:
    - single revision, with nothing after the last `%%EOF`
    - pages match the source in number and size
    - no document-level content
    - no page objects outside the page tree
    - each redacted page is exactly one image, with no annotations
    - text removed
    - areas render solid black
  - **How the text check works:**
    - The secrets are the source's text under each area, plus any extra terms the caller gives.
    - Each secret is looked for in PDFium's text extraction of the output.
    - It is also looked for in every string, every content-stream string operand, and every other stream in the file. Unreferenced objects are included, through a qpdf rewrite that keeps them.
  - **`ContentStrings`**, a small content-stream lexer: literal and hex strings, comments, and inline images skipped.
- **qpdf:** `QpdfEdit`, an object-level editor (also used by the fixture generator), with `OpenEverything`, `AllObjects`, dictionary keys, string and stream reads, and stream replacement.
- **PDFium:**
  - `GetTextInArea`: the text runs whose character centres are inside a rectangle
  - page import by range
  - page sizes on the writer
  - a total object count
  - text-object and font bindings for the probe
- **Fixture `canary-document-level.pdf`:**
  - CANARY-07 drawn as vector rectangles, with no text
  - canaries in the Info title (08), XMP metadata (09), a JavaScript open action (10), a bookmark (11), structure-tree alt text (12), and a hidden layer (13)
  - a link from page 2 to page 1
- **`GlyphProbeTests`:** explicit-only tests for the v2 question.

### Evidence
CI at `29ed037` (run 41338226605) passed on Windows x64, Linux x64, and macOS arm64: locked restore, Release build, tests, fixture generation, and worker self-test. 61 tests pass, 22 of them new; the three probe tests are explicit-only and were run by hand on Windows. All 10 fixtures, including the qpdf-written `canary-document-level.pdf`, are byte-identical on the three OSes.
- **Every canary fixture**, redacted at its manifest target, loses every canary from page text, from the decoded bytes, and from attachments. The exception is CANARY-03, which by design stays on page 2, where its shared form XObject is still used. The verifier reports that as a warning.
- **Unaffected pages keep their text.** Output is byte-identical across runs.
- **Negative controls:** each must be rejected, by the check meant for it:

| Control | Rejected by |
|---|---|
| Black box drawn over the text, full save | text removed; redacted page is one image (and coverage passes, as it should) |
| Text object deleted and box drawn, saved incrementally | single revision. The current revision is clean, but cutting at the first `%%EOF` gives back the original |
| A correct redaction, then an incremental update | single revision, and nothing else |
| A correct redaction, then the canary set as `/Title` | document-level content; text removed |
| A correct redaction, then a note annotation with the canary | redacted page is one image; text removed |
| A correct redaction, then a link to a page outside the page tree | stray pages |
| A correct redaction, checked against an area that was never filled | areas covered |

- **PDF syntax is not reported as text:** "Page", "Type", "Resources", "Tf", "Do", "12" and "0" are all reported absent from a correct redaction.
- **Cost:**
  - Redacting 2 pages of the 2,000-page fixture takes 5.5 s: 0.8 s to rebuild and scrub, and 4.7 s to verify. The output grows from 4.9 MB to 6.0 MB.
  - A one-page text fixture grows from 1.3 KB to 229 KB.

### Findings
30. **The rebuild method held on every canary location in the plan,** with nothing found by either reader.
    - Rendering costs the redacted pages their text, links and sharpness. Each such page adds about 230–560 KB in these fixtures, more where it has images or colour.
    - UI copy must say this before the user applies a redaction (`docs/04-UI-COPY-AND-PRODUCT-RULES.md`).
31. **A raw byte search cannot verify redaction: it matches PDF syntax.**
    - A redacted "Page" would always be found in `/Type /Page`, and "12" in `12 Tf`. Decoded image data would match short secrets by chance.
    - So the verifier searches text-bearing places only: strings, content-stream string operands, and the data of other streams such as metadata, attachments and scripts. It skips images, font programs and ICC profiles. Images are checked by rendering instead.
    - **Known blind spots:**
      - text held in names or in inline image data
      - Type 3 glyph procedures, which are searched raw
      - fonts whose codes are not their characters (for drawn text, text extraction covers this)
32. **PDFium extracts text from hidden layers.** CANARY-13, in a layer that is off by default, comes back from `GetPageText`.
    - That makes the verifier and `GetTextInArea` more cautious, which is right.
    - But S2's reading elements would read hidden text aloud to screen-reader users when sighted users don't see it. Filter text by layer visibility before Milestone 1. **Done 2026-11-06,** with limits ("After the spike").
33. **PDFium's page copy drops references to pages it is not copying.** A link on a kept page to a redacted page arrives with no `/Dest`. This was checked directly, because a deep copy would have brought the whole original page back.
    - The redactor drops such links itself too, so it does not depend on PDFium's behaviour.
    - The stray-pages check catches the failure if either safeguard is lost.
34. **qpdf keeps the first half of an existing file ID, and PDFium's is random.** Redaction output was not reproducible until the scrub removed `/ID`, so that qpdf derives both halves from the content. The root cause is the same as finding 2.
35. **The verifier's limits are deliberate and fail closed:**
    - Only text with its character centre inside an area is a secret. Partly covered characters are still rendered away, but not checked.
    - A secret that also appears on a kept page is only a warning.
    - A secret that happens to match something on a kept page that is not page text, such as a form value, makes verification fail, and the redaction is refused.
36. **Glyph-level probe (v2): yes, for text PDFium can map back from Unicode.**
    - **Method:** remove "CANARY-01" from the middle of "Name: CANARY-01 Phone: 555-0100" by replacing its text object with two new ones, in the same font, size and matrix, placed at the original character origins.

    | Font | Text set | Kept characters moved | Pixels changed outside the area | Ink left inside | Secret in bytes |
    |---|---|---|---|---|---|
    | Helvetica (standard, not embedded) | yes | 0 pt | 0 | 0 | no |
    | Arial, embedded TrueType | yes | 0 pt | 0 | 0 | no |
    | Arial, embedded as a CID font (Identity-H) | yes | 0 pt | 0 | 0 | no |

    - **Caveats:**
      - PDFium can set a text object's character codes (`FPDFText_SetCharcodes`) but has no call to read them. So the probe went through Unicode, and depends on the font mapping each character back to one code. That holds for these fonts, but not for fonts without a ToUnicode map, ligatures, codes that share a character, or Type 3 fonts.
      - The test text has no kerning or character spacing (`TJ` offsets, `Tc`, `Tw`), so drift inside a kept run is untested.
      - Images and vector art under an area need separate handling.
    - **v2 route:** parse content streams ourselves (`ContentStrings` is a start) to get the codes and their positions, then rebuild with `FPDFText_SetCharcodes`.
    - **Side finding:** PDFium's own simple-TrueType embedding makes `-` extract as U+00AD (soft hyphen). The verifier normalises soft hyphens and no-break spaces.
37. **The spike runs redaction in-process; the product must run it in the worker.** It parses untrusted files with PDFium and qpdf, and on Linux and macOS a qpdf C++ exception ends the process (finding 28). Milestone 1 needs worker messages for redact and verify.

### Still open for S5
- ADR-0006, v1 redaction method and verification: proposed (`docs/adr/0006-v1-redaction-method.md`)
- moving redaction into the worker: done 2026-11-06 ("After the spike")

## S6 — Packaging in CI (2026-11-01)

### Built
- **`tools/package/`** (Windows PowerShell 5.1 and PowerShell 7, plus one POSIX sh script):
  - `publish.ps1`: publishes the app and the worker for one runtime identifier into one folder, as NativeAOT (for packages) or self-contained ReadyToRun (for comparison only). Symbols go to a separate folder. It fails if the worker, PDFium or qpdf is missing.
  - `package-windows.ps1`: an MSIX for a full-trust desktop app, with a `pdf-suite` execution alias. It is packed with `makeappx` and signed with `signtool`. The test certificate is created in memory on every run and never enters a certificate store; its public part is written next to the package.
  - `package-macos.ps1`: `PDF Suite.app` with an `Info.plist` and an icon. Every Mach-O file is signed ad hoc with the hardened runtime. The DMG has the usual link to Applications.
  - `package-linux.ps1`:
    - an AppImage, built with appimagetool and the AppImage runtime at pinned, SHA-256-verified versions
    - a .deb that installs to `/opt/pdf-suite`, declares the libraries the app loads at run time, and adds a `pdf-suite` command and a desktop entry
  - `smoke-test.ps1` and `smoke-test.sh`: launch the app several times with `--smoke-test` and report startup times as public annotations.
  - A placeholder icon drawn in code, with no image files or third-party art.
- **App:**
  - `--smoke-test` opens a file, waits until the first page is sharp, and asks the worker for qpdf's version. It writes startup times to JSON, and exits with 0 only if both worked.
  - The benchmark's JSON no longer uses reflection (finding 38). It has the same fields, plus the code generation mode.
  - `IsAotCompatible` runs the AOT and trimming analyzers on every build.
- **Worker:** loads qpdf, so packages carry it for each runtime identifier, as the product will need (finding 37). A new `GetEngineInfo` request returns qpdf's version, and `--self-test` prints it.
- **Package workflow** (`.github/workflows/package.yml`), on pushes that change code:
  - one job per OS: locked restore, fixtures, NativeAOT and ReadyToRun publishes, smoke tests of both folders, the packages, and a list of linked libraries
  - then each package installed on a fresh runner and launched:
    - the MSIX on Windows, after trusting the test certificate
    - the DMG on macOS, with a Gatekeeper assessment
    - the .deb in a bare `ubuntu:24.04` container
    - the AppImage on Ubuntu
- One new test: the worker reports the qpdf version it loaded.

### Evidence (Windows, development machine)
- **Build and tests:** the Release build has 0 warnings, and 62 tests pass (the 3 explicit probes are skipped). The worker self-test prints `"qpdfVersion":"12.4.2"`.
- **NativeAOT:** both programs publish with no AOT or trimming warnings, with warnings treated as errors.
- **MSIX:** built and signed, 22.4 MB. It was not installed on this machine: that needs the test certificate imported into the machine's TrustedPeople store, which is left to CI's throwaway runners.
- **Startup:** smoke tests on `large-2000-pages.pdf`, run back to back, 4 runs each. Times are from process start to the window, and to the first page sharp:

| Build | Folder | First run | Warm (runs 2–4) |
|---|---|---|---|
| NativeAOT (packages) | 53.6 MB, 15 files | 393 ms / 682 ms | 168–186 ms / 307–341 ms |
| Self-contained ReadyToRun | 133.8 MB, 238 files | 1,363 ms / 1,881 ms | 373–431 ms / 578–650 ms |
| Framework-dependent build output (JIT), as measured in S0 and S1 | Needs .NET installed | 1,118 ms / 1,615 ms | 641–646 ms / 951–958 ms |

- **What "first run" means here:** the first launch after publishing, with the files still in the OS cache. The very first NativeAOT launch, straight after compiling, took 525 ms / 1,176 ms. True cold starts are what the CI install jobs measure, on fresh runners.
- **The full S1 benchmark on the NativeAOT build** (two runs, GPU, 256 px tiles):
  - p95 frame interval of 17.4–18.5 ms at every zoom level, no blank tiles, and peak app plus worker memory of 541 MB. Scrolling is no worse than with the JIT.
  - The first run had one 238 ms stall at 400%. GC paused for only 12 ms in that whole phase, so GC does not explain it. The second run had no stall over 50 ms; its worst frame was 37 ms.
  - Treated as an outside interruption, not reproduced. Worth watching in Milestone 1.
- **What the NativeAOT folder holds (win-x64):**
  - the app: 18.0 MB
  - Skia: 11.1 MB
  - PDFium: 7.2 MB
  - qpdf: 7.1 MB, with OpenSSL linked statically
  - ANGLE: 5.1 MB
  - the worker: 1.8 MB
  - HarfBuzz: 1.7 MB
  - the MSVC runtime, from qpdf's archive: 1.6 MB

  Symbols kept out of packages: 184 MB.

### Evidence (CI)
The Package workflow at `81ab077` (run 41356497705) passed on all three OSes. The CI workflow on the same commit (run 41356497684) also passed: 62 tests, and all 10 fixtures byte-identical across the OSes. Both passed again at `63e874f` (runs 41357234415 and 41357234604), which gave a second set of first-launch times.

**Every package installed on a fresh runner and launched.** Each launch rendered the first page of the 2,000-page fixture and loaded qpdf 12.4.2 in the worker. The first launch after installing is a cold start. Times are from process start to the window, and to the first page sharp:

| Package and machine | Package | Installed | How it was installed | First launch, two runs | Warm (runs 2–3) |
|---|---|---|---|---|---|
| MSIX, Windows Server 2025 runner | 22.4 MB | 53.7 MB | Test certificate trusted, then `Add-AppxPackage`. Started through its `pdf-suite` alias, then uninstalled cleanly | 231 / 395 ms; 399 / 736 ms | 114–151 / 248–309 ms |
| DMG, macOS arm64 runner | 24.1 MB | 51 MB | Mounted, and the app copied to /Applications | 285 / 1,054 ms; 617 / 2,418 ms | 109–179 / 286–482 ms |
| .deb, bare `ubuntu:24.04` container | 18.4 MB | 55 MB | `apt install`, which pulled in 18 packages, ICU and DejaVu fonts among them. Removed cleanly | 71 / 146 ms; 105 / 194 ms | 54–91 / 128–179 ms |
| AppImage, Ubuntu 26.04 runner (`ubuntu-latest`) | 21.6 MB | Runs in place | None: it mounted itself with FUSE | 3,561 / 3,818 ms; 1,698 / 1,969 ms | 176–187 / 283–290 ms |

- **macOS signing:** the installed bundle verifies (`valid on disk`, `satisfies its Designated Requirement`), with the flags `adhoc,runtime` and no team identifier. Gatekeeper's assessment: `rejected`.
- **Published folders, in the packaging jobs** (first run at `81ab077`). These are not fresh machines: the files had just been written. Window / first page sharp:

| OS | NativeAOT folder | First run | Warm | ReadyToRun folder | First run | Warm |
|---|---|---|---|---|---|---|
| Windows x64 | 53.6 MB, 15 files | 91 / 212 ms | 82–93 / 196–199 ms | 133.8 MB, 238 files | 534 / 762 ms | 375–392 / 602–648 ms |
| macOS arm64 | 51.2 MB, 7 files | 398 / 722 ms | 151–155 / 409–417 ms | 142.6 MB, 229 files | 1,259 / 3,257 ms | 318–341 / 724–833 ms |
| Linux x64 | 54.6 MB, 15 files | 93 / 180 ms | 96–97 / 184–185 ms | 133.2 MB, 238 files | 2,022 / 2,179 ms | 378–401 / 523–564 ms |

- **Linked libraries on Linux (`ldd`):**
  - Both NativeAOT executables need only glibc (`libc`, `libm`).
  - Skia needs fontconfig, FreeType and their dependencies.
  - qpdf needs libstdc++, zlib, and GMP, through its bundled GnuTLS and Nettle.
- **Build time:** a packaging job takes 2–3.5 minutes. Compiling both programs with NativeAOT takes 37–73 s of that.

**Fixed after the first run,** and confirmed by the run at `63e874f`:
- **A false failure:** the Windows step that smoke-tests the published folders reported a failure although all six runs passed. `smoke-test.ps1` ended without setting an exit code, so the step read a stale `$LASTEXITCODE`. The step does not block, so the run still passed.
- **Two undeclared libraries:** the .deb now also declares `libgmp10` and `libfreetype6`, which `ldd` showed are linked (finding 47).

### Findings
38. **NativeAOT works for both programs.**
    - The worker already compiled cleanly.
    - The app's only blocker was the S1 benchmark, which serialised anonymous types by reflection (IL2026, IL3050). It now builds `JsonObject`s.
    - Avalonia 12.1.4, SkiaSharp, HarfBuzzSharp and the project's own bindings produced no AOT or trimming warnings, with warnings treated as errors.
    - **One trap:** a `JsonArray` collection initialiser calls the generic `Add<T>`, which is not AOT-safe, unless the elements are typed `JsonNode`. The analyzer caught it at build time.
39. **Startup meets the PRD target (under 3 s) with room to spare.** Warm, NativeAOT shows the first page sharp in about 0.3 s, against 0.6 s for ReadyToRun and 0.95 s for the plain build output.
    - S0's 2.9–3.5 s warm for the framework-dependent build did not reproduce: the same build took 0.64 s today.
    - Defender scanning a new build, or machine load, would explain it, but that is not verified.
40. **NativeAOT also makes the app smaller:** 53.6 MB in 15 files, against 133.8 MB in 238 files for ReadyToRun. About 32 MB of the folder is native libraries the project does not compile: Skia, PDFium, qpdf, ANGLE, HarfBuzz and the MSVC runtime.
41. **`PublishAot` in a project file makes the lock files depend on the OS.**
    - Restore then adds the build machine's runtime identifier (`win-x64` here). Lock files written on Windows would fail CI's locked restore on Linux and macOS (`NU1004`).
    - So `PublishAot` is passed at publish time, and `IsAotCompatible` runs the analyzers in ordinary builds.
42. **Publishes restore packages that no lock file checks.** For NativeAOT, that is the ILCompiler package for the runtime identifier (36 MB for `win-x64`), at the version the pinned SDK sets. Locking it needs one of:
    - declaring every runtime identifier in the projects, which adds all three ILCompiler packages to every restore
    - committing a lock file per runtime identifier for every project in the publish graph (15 files)

    Decide in Milestone 0, in ADR-0005. It is recorded in the inventory.
43. **Building NativeAOT locally on Windows needs `vswhere.exe` on PATH.** The ILCompiler build targets call it by name, but the Visual Studio installer does not put it on PATH. The error is misleading ("The filename, directory name, or volume label syntax is incorrect"). `publish.ps1` adds it.
44. **A trimmed fallback does not work with two programs in one folder.** Each is trimmed against its own code, which produces two different versions of the same framework files. So the comparison build is ReadyToRun and untrimmed. If NativeAOT ever had to be dropped, the worker would need its own folder and runtime.
45. **Every AppImage contains LGPL-2.1 code:** the AppImage runtime links libfuse statically. Shipping an AppImage brings the same kind of obligations as qpdf's Linux libraries (finding 26). The .deb does not add this.
46. **Ad hoc signing with the hardened runtime is enough to run, not to distribute.**
    - The app loads its own ad hoc signed libraries under the hardened runtime without the library-validation exception, and NativeAOT needs no JIT entitlement. So the bundle needs no entitlements at all.
    - But Gatekeeper rejects it, so a copy downloaded with a browser would not open normally. Distributing on macOS needs a Developer ID and notarisation, through the Apple Developer Program.
47. **A bare container checks a .deb's dependencies only as far as its base image goes.**
    - apt installed everything the package declares, and the app ran.
    - But `ldd` found two linked libraries the package did not declare directly. FreeType (Skia) came in through fontconfig, and GMP (qpdf's crypto) is part of every Ubuntu base image. Both are now declared.
    - So dependency lists should come from `ldd` plus the libraries loaded at run time, not from a passing install alone.
48. **The AppImage's first launch took 3.6 s and 1.7 s in two runs, against 0.07–0.62 s for the installed packages.**
    - Its warm launches took 0.18 s, still two to three times the .deb's 0.05–0.09 s.
    - The likely cause is reading the compressed image through FUSE, cold on the first launch. That is not investigated.
    - It is the only package measurement over the PRD's 3 s target, and it is a first launch on a CI runner.
49. **NativeAOT has the edge on every OS.**
    - Over two runs, a first launch after installing reached the first page in 0.15–2.4 s. The slowest was macOS, at 2.4 s in one run and 1.05 s in the other, so cold starts on CI runners vary a lot between runs.
    - In the packaging jobs, the ReadyToRun build's first runs took 1.3–3.3 s to the first page. That is 3.6 to 12 times as long as NativeAOT in the same job.
    - This supports NativeAOT as the v1 build mode (ADR-0005).

### Still open for S6
- **ADR-0005,** packaging and distribution channels: proposed (`docs/adr/0005-packaging-and-distribution.md`). It covers the build mode, formats, signing and notarisation, and locking publish restores (finding 42).
- **Signing:** notarisation, which needs the Apple Developer Program, and a real Windows signing route (spike plan §9).
- **The AppImage:** its slow first launch (finding 48), and the LGPL obligations it brings (finding 45).
- **A real Linux desktop:** the Linux packages ran under Xvfb only. A Wayland session with XWayland is still to be tried, together with the S1 Linux measurements.
- **Other architectures:** packages for Linux arm64, macOS x64 and Windows arm64 were not attempted.

## After the spike (2026-11-06)
Work done after the spike, which the go/no-go section carried into Milestone 1. It does not depend on any decision still open.

### Redaction and qpdf operations in the worker (findings 13, 28 and 37)
- **New worker requests:**
  - `Redact` returns the redacted file and the verifier's report. If verification fails, it returns the report and no file.
  - `Transform` covers encrypt, decrypt, set metadata and rewrite.
  - `GetMetadata` reads document information.
- **What travels:** each request carries the whole document, so it does not depend on what the worker has open.
- **Errors:**
  - A wrong or missing password is reported like a password-protected open.
  - Any other qpdf failure crosses the process boundary as its error code only, because qpdf's own message can quote the file.
- **Client:** `WorkerClient` has `RedactAsync`, `EncryptAsync`, `DecryptAsync`, `SetMetadataAsync`, `RewriteAsync` and `GetMetadataAsync`, with hang allowances sized for long operations.
  - The worker handles one request at a time, so a long redaction holds up rendering for any document open in the same worker. Whether heavy operations get their own worker is a Milestone 5 design question.
- **Shared types:** the redaction and permission types moved to `PdfSuite.Pdf.Abstractions`, so the app needs no engine reference to ask for these operations.
- **No document text in exception messages:** `RedactionFailedException`'s message now names only the failed checks, because exception messages end up in logs. The report holds the details, which can quote the document.
- **Finding 13 fixed:** when an open is cancelled after the worker has started it, the client closes the document as soon as the late answer arrives.

**Evidence (Windows, development machine):**
- **9 new tests,** each through a real worker process:
  - Redaction gives byte-identical output to redacting in-process, and returns the verifier's warnings.
  - A failing verification returns its failures and no file, and the exception message does not contain the secret.
  - Encryption needs the password in PDFium, and decryption removes it. A wrong password is a password error.
  - Metadata and rewrites match in-process output byte for byte.
  - Invalid data is a qpdf failure, and the worker keeps serving.
  - 30 opens cancelled at different moments leave no document open.
- **The finding 13 test was checked against the bug:** with the fix disabled, it failed in 3 runs out of 3, leaving 11–12 of the 30 documents open.
- **Totals:** 71 tests pass (3 explicit probes skipped), and the Release build has 0 warnings.
- **NativeAOT:** once the SDK was bumped (finding 50), the worker with these requests published with no warnings, and the smoke test passed.
- **CI at `97cefbe`** (runs 42162820337 and 42162820338) passed on all three OSes, with these requests and the patched runtime.
  - Every package was rebuilt on runtime 10.0.14. Each one passed NuGet's audit, installed on a fresh runner, and launched.
  - Launch times were in line with S6: the Windows NativeAOT folder reached the window in about 0.1 s, warm.
  - Two first launches were slower than in S6. The AppImage took 4.9 s to the window, its third first launch over 3 s (finding 48). The DMG took 2.8 s to the first page.

### Worker memory limits on Linux and macOS (finding 12)
- **Linux:** the worker caps its own memory with `RLIMIT_DATA` before it reads any request. The app passes the limit as `--memory-limit`.
  - `RLIMIT_DATA` counts private writable memory as it is committed (Linux 4.7 and later). Address space that is only reserved does not count, so the .NET runtime's large reservations are not a problem. They would be under `RLIMIT_AS`, which is why S1 expected trouble there.
  - The hard limit is lowered too, so a compromised worker cannot raise it again.
  - An allocation over the limit fails, and the worker reports OutOfMemory and keeps serving, as on Windows.
- **macOS:** neither limit is enforced for mapped memory there. So the client reads the worker's memory footprint (`proc_pid_rusage`, the figure Activity Monitor shows) every 250 ms, and stops the worker when it goes over.
  - This cap is softer: the worker is stopped rather than refused one allocation, and it can overshoot between checks.
  - Its session restarts it like any crash, and gives up after 3 restarts in 30 s.
- **Test:** the memory-limit test now runs on every OS, with the outcome each one should have.
  - Windows and Linux must refuse a 512 MB allocation under a 256 MB cap, then serve a 32 MB one.
  - macOS must stop a worker that holds 768 MB.
  - It passes on all three OSes (CI at `2357e4e`, run 42163460286). On Linux and macOS, 70 tests passed and 4 were skipped (the 3 explicit probes, and the Windows-only print test), so it ran there.

### Hidden-layer text and screen readers (finding 32)
- **The engine gap:** PDFium has no API for layer visibility. It does expose content marks: text inside `/OC /Layer BDC … EMC` carries an `OC` mark whose parameters are the layer's dictionary, so the layer's `/Name` is readable.
- **qpdf supplies the states:** `QpdfStructure.HiddenLayerNames` reads the default configuration (`/OCProperties /D`, with its `/BaseState`, `/ON` and `/OFF`) and returns the names of the layers that are off.
- **The reading lines use it:** `PdfiumDocument.GetTextLines`, which feeds the screen-reader elements, now builds each line from its visible characters only.
  - The worker asks qpdf for the hidden layer names only when a line first meets an `OC` mark. Documents without layers never touch qpdf here.
  - If qpdf cannot read a file that PDFium could, every layer counts as visible.
- **Unchanged on purpose:** `GetPageText` and `GetTextInArea` still include hidden text, because redaction must find and remove it.
- **Limits, all of which fail towards reading the text rather than hiding it:**
  - membership dictionaries (OCMDs), which have no `/Name`
  - form XObjects and images hidden by their own `/OC` entry
  - usage rules (`/AS`)
  - a name shared by a layer that is on and one that is off, which counts as visible

**Evidence (Windows):**
- **4 new tests:**
  - Through the worker, CANARY-13 is missing from the reading lines, while `GetPageText` still has it.
  - Negative control: the same file with the layer switched on is read in full, so the filtering is what removes it.
  - `HiddenLayerNames` handles both `/BaseState` modes.
  - A name shared with a visible layer is not hidden.
- **Totals:** 78 tests pass, and the fuzz test's results are unchanged.

### Reading order for tagged pages (finding 16)
- **What a tagged page declares:** the order it should be read in. Its structure tree lists marked content (`/P <</MCID n>> BDC … EMC`) by ID in that order.
- **What PDFium exposes:** the page's structure tree (`FPDF_StructTree_GetForPage`) and each text object's marked-content ID.
- **How the reading lines use it:** `GetTextLines` walks the tree depth-first to rank the IDs, gives each line the rank of its earliest tagged character, and sorts the lines by rank.
  - Lines with no tag, such as artifacts like running headers, stay right after the tagged line before them. Nothing is dropped.
  - The sort is stable, so lines of the same marked content keep their order on the page.
- **When it applies:** only when the file says it is tagged (`/MarkInfo /Marked true`); PDFium ignores the structure tree otherwise. Untagged pages keep the order the file draws them.
- **Evidence:**
  - A test page draws a running header, then paragraph 2, then paragraph 1, and its structure tree puts paragraph 1 first. It reads header, paragraph 1, paragraph 2.
  - Negative control: the same page without the tree reads in drawing order.
  - 80 tests pass.
- **Still open:** reading order for untagged pages, which needs layout analysis (a dependency decision). Whether artifacts such as running headers should be read at all is a product decision; PDF/UA says they are not content.

### No network use while reading (PRD §9; Milestone 1 acceptance)
- **The check:** the Package workflow runs the installed .deb's smoke test under `strace` in the bare Ubuntu container. It fails if the app or its worker opens an IPv4 or IPv6 socket while opening, rendering and loading qpdf. Local sockets, for X11 and D-Bus, are allowed.
- **Negative control:** bash connecting to a closed local port, under the same trace, must be caught, or the step fails as proving nothing.
- **Only Linux:** Windows and macOS would need their own tracing (ETW, `dtrace` or the network extension APIs). They are not covered yet.
- **First run (CI at `70680f1`, Package run 42204462613): passed.**
  - It traced 19 socket and connect calls while the app opened the fixture, rendered it and loaded qpdf. None was for an IP socket.
  - The control was caught.
  - The launch under the trace reached the window in 274 ms and the first page in 424 ms.

### Text search in the engine and the worker (Milestone 1)
- **The engine:** `IPdfDocument.FindText(page, query, matchCase, wholeWord)` returns each match's text and its rectangles, from PDFium's `FPDFText_Find*`.
- **The worker:** serves it one page per request, so tile requests are served between the pages of a long search. The client's method is `FindTextAsync`.
- **Hidden layers:** like the reading lines, the worker leaves out matches in text a sighted reader cannot see (finding 32).
- **Evidence:** 4 tests.
  - The match's text and position, against the known layout of `canary-text.pdf`.
  - The case and whole-word options.
  - The worker returns exactly the engine's matches and rectangles.
  - A hidden-layer canary is not found through the worker, but is found by the engine on its own, as a negative control.
- **Not built at first:** the search user interface, and searching a whole document as one request.
- **Added 2026-11-07: whole-document search and the search bar.**
  - **`DocumentSearch`** (in `PdfSuite.Pdf.Remote`, so it is tested without the UI) searches every page for one query, a page per worker request, starting at the page in view and wrapping round.
    - Next and previous move through matches in document order, wrapping at either end. A move that needs a page not searched yet has that page searched next.
    - A match's position ("3 of 12") is given once every page before it has been searched.
    - A page the engine cannot read counts as having no matches and is reported. A page that stops the worker twice ends the search, keeping what it found.
  - **The session asks again after a crash.** `DocumentSession.FindTextAsync` waits for the session to restart a worker that stopped mid-request, then asks the new one once.
    - Asking at once would fail: the client fails its requests up to 2 s before it raises `Exited`, so the session has not yet replaced the dead worker.
  - **The search bar** (`SearchPanel`), from the copy rules (§5): Ctrl+F or **Search** opens it. Enter or F3 finds the next match, Shift+Enter or Shift+F3 the previous one, and Escape or **Close** closes it.
    - Match case and whole words. Changing either searches again; typing a new query clears the old highlights.
    - Matches on visible pages are highlighted, and the current one also has an outline, so it does not rely on colour alone.
    - The status line is a polite live region, so screen readers can announce counts without moving focus.
    - Closing it moves keyboard focus to the reading line holding the current match, so reading carries on from there.
    - The copy rules' OCR hint is not shown, because the build has no OCR (§3: only show tools in the build). Messages the copy rules did not have are proposed in §5.
  - **The packaged smoke test searches too.** After the first page, it searches all pages for the last page's first line, moves to the match on the last page, and waits until it is drawn on a sharp page. So every package in CI checks search through its NativeAOT build. The run fails if it is not found or not shown.
  - **Evidence (Windows):**
    - **11 new tests,** all passing; 95 passed in all, with the 3 explicit probes skipped.
      - Scripted pages check the search order, finding the first match before earlier pages are searched, wrapping, searching a wanted page next, cancelling, a page the engine cannot read, and the worker giving up.
      - Through the worker: a 120-page document, against the engine's counts.
      - A worker killed after 10 of 200 pages: the search completes with the engine's count, on a new worker. **With the session's retry removed, this test failed three times out of three**, ending as Failed instead of Complete.
    - **The smoke test,** on the local Release build (JIT): all 2,000 pages searched in 4.5–5.4 s, about 2.5 ms a page; the match was drawn on a sharp page within 60 ms.
    - **Driven through UI Automation** (invoke, value and toggle patterns, no keystrokes):
      - "Match 1 of 1." for "Page 1500 of 2000", with the view on page 1500 and the match outlined.
      - "No matches found…" for QUARTERLY with match case, then 35,160 matches without it, from the match on the page in view (26,348).
      - Next and previous step by one and cross pages. After **Close**, focus is on the reading line "Page 1500 of 2000".
  - **CI at `42afa16`** (CI run 42435165541; Package run 42435165556): every job passed.
    - **Tests:** all 98 ran on each OS, the 11 new ones included. 95 passed on Windows; 94 on macOS and Linux, which also skip the Windows-only print test.
    - **Search worked in every package,** three runs each: one match, on page 2,000, drawn on a sharp page in 32–109 ms. Time to search all 2,000 pages:

    | Where | Search, 2,000 pages |
    |---|---|
    | NativeAOT folder, in the build job: Windows / Linux / macOS | 1.9–2.0 s / 2.4 s / 4.6–5.5 s |
    | MSIX, Windows | 2.6 s |
    | DMG, macOS | 2.6–3.4 s |
    | AppImage, Ubuntu 26.04 | 2.6 s |
    | .deb: Ubuntu 22.04 / 24.04 / 26.04 | 2.9 s / 1.8–1.9 s / 2.4 s |
    | .deb: Debian 12 / 13 | 3.0 s / 2.5 s |
    | .deb under `strace` (network check) | 3.3 s |

    - **The network check** passed with the search in the traced run: no IP socket while opening, rendering and searching.
    - The AppImage's first launch took 3.7 s to the window, so four of its ten runs were over 3 s (finding 48).
  - **Not verified:**
    - the keyboard shortcuts, which need keystrokes sent to the desktop
    - what a real screen reader announces
    - the search bar on macOS and Linux: CI checks the search and the highlight there, not the bar

### Password prompt (Milestone 1, 2026-11-07)
- **The viewer asks for a password** when the worker reports one is needed, with the copy rules' dialog (§6): heading, body, a Password field, **Unlock PDF** and **Cancel**.
  - A wrong password keeps the dialog open with "The password could not unlock this document. Check the password and try again.", announced at once to screen readers.
  - Cancel leaves whatever document was open before.
  - Any other failure closes the dialog and gets the usual "We couldn't open this PDF…" message.
- **The password** is held only by the session, in memory, so a restarted worker can reopen the document. It is never logged, saved or sent (decisions for review, D2).
- **Evidence (Windows):**
  - 2 tests: a session refuses no password and a wrong one, and opens with the right one; and it reopens a protected document after the worker is killed.
  - Through UI Automation, on an AES-256 file: the dialog appeared; a wrong password kept it open with the message; the right one opened the document on page 1 of 3; Cancel, in a second launch, left "No document open."

### Document properties (Milestone 1, 2026-11-07)
- **The engine and the worker** report a document's information keys, PDF version, encryption, permissions and whether it is tagged (`GetDocumentInfo`, a new worker request), read by PDFium from the open document.
  - **Permissions** are those the document was opened with: all of them for an unencrypted document or one opened with its owner password. The app uses "copy text" (decisions for review, D1).
  - **Tagged** follows ISO 32000-1 §14.8.1: `/MarkInfo << /Marked true >>` in the catalog. A structure tree alone, as in the document-level canary fixture, does not count.
  - **PDF dates** (`D:20261107143000+01'00'` and its shorter forms) are parsed by `PdfDate`, in the abstractions.
- **The Document Properties dialog** (button, or Ctrl+D) shows them with the file size and page size. Its wording is proposed in the copy rules (§5a).
- **Evidence (Windows):**
  - 17 tests:
    - non-ASCII metadata, the same through the worker as from the engine
    - tagged only with `/MarkInfo`, which a first test got wrong by expecting the structure tree to count
    - a copy-restricted document's permissions, lifted by its owner password
    - 14 date cases, valid and invalid
  - Through UI Automation:
    - On a copy-restricted file: "Encrypted", with Copying text, Changing the document and Page assembly "Not allowed".
    - On the 2,000-page fixture: 2,000 pages, and "pages differ in size".

### Page navigation and zoom (Milestone 1, 2026-11-07)
- **A second toolbar row**, from the copy rules' reader toolbar (§4) and View menu (§3):
  - Previous and next page, the current page out of the total (Ctrl+G focuses it; Enter goes to the typed page), and zoom out and in.
  - **Fit Width**, **Fit Page** and **Actual Size**, also Ctrl+2, Ctrl+0 and Ctrl+1 (decisions for review, D7). A fit is kept as the window is resized, without moving the reading position.
- **A layout bug, found and fixed:** in a document with wider pages elsewhere (this fixture's landscape pages), a fitted page was shown shifted and cut off. That happened because pages are centred across the widest page; a fit now also centres the view.
- **Evidence (Windows), through UI Automation:**
  - Next and previous changed the page by one, and the page box followed; Previous was disabled on page 1.
  - Fit Width gave 120%, Fit Page 61% and Actual Size 100%; zoom in and out stepped by 25%. A screenshot showed the fitted page whole.
  - **Not verified:** typing a page number, which needs Enter pressed.

### Text selection and copy (Milestone 1, 2026-11-07)
- **The worker sends a page's visible characters** with their boxes (`GetCharacters`, a new request). It uses PDFium's loose character boxes, so every character on a line has the same height. Text in hidden layers is left out, as for search and the reading lines.
- **The selection logic** is in the abstractions (`TextSelection`), so it is tested without the UI: the caret nearest a point (nearest line first), words, highlight rectangles joined per line, and the copied text with the engine's line breaks.
- **In the viewer:**
  - Drag to select, across pages too; double-click selects a word; a plain click or Escape clears.
  - Ctrl+A selects the page in view (decisions for review, D5). Ctrl+C or the **Copy** context menu copies.
  - With nothing selected, Ctrl+C copies the focused reading line, so keyboard and screen-reader users can copy.
  - **Copying honours the document's "copy text" permission** (D1). Messages are proposed in the copy rules (§5b).
- **A bug, found and fixed:** on Windows, copied text vanished from the clipboard when the app closed, because the data stayed the app's own. The app now flushes the clipboard after copying (`IClipboard.FlushAsync`).
- **The packaged smoke test** now also selects the text of the page it moved to and checks that it holds the search phrase.
- **Evidence (Windows):**
  - **7 tests:**
    - characters and boxes against the canary fixture's known layout
    - the same through the worker as from the engine
    - hidden-layer text left out, with the engine on its own as the negative control
    - a dragged word, a backwards drag and a drag past the line's end
    - a double-clicked word joined by a hyphen
    - a two-line selection highlighted line by line
    - the nearest-caret rules on synthetic characters
  - **Through the app's own window,** with mouse messages posted to it (the real cursor and focus untouched):
    - Dragging over "Simple text page 1" and choosing **Copy** put exactly that on the clipboard; a double-click copied the word "text".
    - On a copy-restricted file the app said "This document does not allow copying text." and left the clipboard as it was.
    - After closing the app normally, the copied text was still on the clipboard. Before the fix, it was gone.
  - **The smoke test,** locally: the 2,000th page's 3,109 characters selected, holding the search phrase.
  - **Not verified:** the keyboard shortcuts (they need keystrokes), and macOS and Linux, until CI runs.

### Close Document, and the open-close-reopen gate (Milestone 1, 2026-11-07)
- **Close Document** (button, or Ctrl+W) closes the document: its search, selection and worker go with it. The viewer never writes to the file it opened, so nothing needs saving.
- **The roadmap's gate,** "test documents can be opened, closed, and reopened without corruption", now has a test. A 30-page document is opened, a tile and a page's text are read, and it is closed. Then:
  - the worker process has ended
  - reopened, the same page renders the same pixels and gives the same text
  - the document's bytes hash the same as before
- **Through UI Automation:** after Close Document the status reads "No document open.", the document's worker process has gone, and the page and zoom controls are disabled.

### Platform command key (2026-11-07)
- **Shortcuts used Ctrl on every OS,** where macOS users expect Cmd (Cmd+C, Cmd+F and so on). PRD §6 asks for platform-appropriate keyboard conventions before a platform is supported.
- **They now use the platform's command key:** Cmd on macOS, Ctrl elsewhere. That covers the window's shortcuts, the view's keys and Ctrl + wheel zoom, the context menu's gestures, and the tooltips, which read "⌘W" on macOS.
- **Not verified on macOS:** nothing here sends keys, and CI does not test shortcuts. On Windows nothing changes, as the command key is Ctrl.

### Fuzz tests, and a weekly audit
- **`FuzzTests`, using a fixed seed** so every run tries the same cases:
  - **The content-stream lexer:** 20,000 random inputs, biased towards its special characters. It never throws, and never returns more bytes than it was given.
  - **A round trip:** 5,000 random strings, written as literal and hex strings, come back exactly.
  - **300 mutated fixtures through a real worker:** each is opened, rendered, read for text, then rewritten with qpdf.
    - An open must end in a document or a `PdfOpenException`.
    - Errors on a page are allowed, because the view handles them.
    - Worker crashes are counted, not failed: they would be engine bugs, contained by the worker.
- **Weekly runs:** CI and the Package workflow also run every Monday, so NuGet's audit sees new advisories when nothing has changed. GitHub disables schedules in a repository with no activity for 60 days.

**Evidence (Windows):**
- **Mutated fixtures:** 204 of 300 opened and 96 were refused as damaged. One first page could not be drawn. qpdf rewrote 237 and refused 63.
- **No worker crashed.**
- **The fuzz tests take 2–4 s.**

### CI at `70680f1` (2026-11-06)
The CI workflow (run 42204462513) and the Package workflow (run 42204462613) passed on all three OSes, with the fuzz tests, hidden-layer filtering, tagged reading order, search and the network check.
- **Tests:** on Windows 84 passed, and the 3 explicit probes were skipped. On Linux and macOS 83 passed and 4 were skipped, adding the Windows-only print test. So every test added since `2357e4e` passed on all three OSes.
- **Runners:** `windows-2025-vs2026`, `macos-26-arm64` and `ubuntu-26.04`. `ubuntu-latest` has been Ubuntu 26.04 since S6, which the S6 table had wrong.
- **Fixtures:** all 10 were byte-identical across the OSes.
- **The fuzz test's counts are not visible in CI:**
  - It writes how many mutated files opened, were refused or crashed the worker to test output, which CI does not show for passing tests.
  - On Linux and macOS it is known that every open ended in a document or a clean refusal. It is not known whether a worker crashed there.
- **No network use:** passed its first run (see above).
- **Every package installed on a fresh runner and launched again** (window / first page sharp):

| Package | First launch | Warm (runs 2–3) |
|---|---|---|
| MSIX, Windows | 265 / 462 ms | 154–162 / 319–403 ms |
| DMG, macOS | 1,008 / 3,527 ms | 257–286 / 779–781 ms |
| .deb, bare `ubuntu:24.04` container | 117 / 188 ms | 75–86 / 163–174 ms |
| AppImage, Ubuntu 26.04 | 632 / 847 ms | 123–128 / 207–218 ms |

- **The AppImage's first launch** reached the window in 0.6 s, against 1.5–4.9 s in the four runs before (finding 48). Its first launches vary more than any other package's.
- **The DMG's first launch took 3.5 s to the first page, its slowest yet** (finding 52). The PRD's 3 s target is for the window, which took 1.0 s.

### Linux releases for the .deb (findings 53 and 54)
- **The glibc floor is measured from the files:**
  - `package-linux.ps1` reads every ELF file's version references (`.gnu.version_r`), which the dynamic loader checks before a program starts. It reads them directly, so packaging needs no binutils.
  - The newest glibc version needed becomes the package's `libc6 (>= …)` dependency. apt then refuses the package on an older system, instead of installing one that cannot start.
  - The newest libstdc++ and libgcc versions are reported, not declared: their package versions do not map simply onto symbol versions. The install jobs check them instead.
  - Each floor is a public annotation, with the files that set it.
- **The .deb is installed and launched on every release whose ICU it accepts:** bare `ubuntu:22.04`, `ubuntu:24.04`, `ubuntu:26.04`, `debian:12` and `debian:13` containers.
  - If apt refuses the package, the annotation gives apt's reason.
  - The network check stays on Ubuntu 24.04 only, because what it checks is the app, not the system.
- **ICU 78 is now accepted** (finding 54).
- **Evidence (Windows):** the reader was run on PDFium, Skia, HarfBuzz and qpdf's Linux libraries, from the NuGet cache and qpdf's pinned archive. For every file, it found the same newest versions as a plain search of the binary. The prebuilt libraries need:
  - glibc 2.34, set by the GnuTLS stack bundled with qpdf (`libgnutls`, `libp11-kit` and `libunistring`)
  - `GLIBCXX_3.4.29` and `CXXABI_1.3.9`, for qpdf

  So the prebuilt libraries fit Ubuntu 22.04 (glibc 2.35) and Debian 12 (glibc 2.36). The NativeAOT executables, linked on the runner, can be measured only in CI.
- **CI at `6d625fb`** (Package run 42209442962; CI run 42209443001 passed, with the same test counts as `70680f1`):
  - **The floor is glibc 2.38, set by `PdfSuite.App` alone.** The worker and every prebuilt library need less. The libstdc++ floor is `GLIBCXX_3.4.29`, from qpdf.
  - **Ubuntu 24.04 and 26.04 and Debian 13:** installed and launched. Ubuntu 26.04 installed `libicu78`, so finding 54 is fixed.
  - **Ubuntu 22.04 and Debian 12:** apt refused the package (`libc6 (>= 2.38)`, against 2.35 and 2.36). Before this change it would have installed and then failed to start, so finding 53 was real.
  - **So the Package workflow now fails** until it is decided whether the .deb supports those two releases. Supporting them means lowering the app's floor; dropping them means removing their ICU versions and install jobs.
  - **What needs 2.38** was not known yet, so the annotation was extended to name the symbols behind each floor.
    - The symbol names were checked on the prebuilt libraries. They give the expected functions: `__libc_single_threaded` at 2.32, `stat` at 2.33, the pthread and `dl` functions at 2.34.
- **CI at `b186887`** (Package run 42215059863; CI run 42215059896 passed): **one symbol sets the floor: `fmod`, in `PdfSuite.App`.**
  - glibc 2.38 gave `fmod` a new default version, so a program linked against glibc 2.38 or later needs it. The runner has glibc 2.43. The old version stays in every glibc.
  - The app probably calls it through the remainder operator on doubles, which NativeAOT compiles to a call to C's `fmod`. The worker does not use it.
  - **So the floor comes from where the app is linked, not from what it needs.** Linked against an older glibc, the app would need only what the bundled libraries need: glibc 2.34, which Ubuntu 22.04 and Debian 12 have.
  - It also means a newer runner image can raise the floor again. The annotation and the install jobs would now show it.
  - The results otherwise matched `6d625fb`: Ubuntu 24.04 and 26.04 and Debian 13 passed, and Ubuntu 22.04 and Debian 12 refused the package.
  - PowerShell 7 on the runner found the same libstdc++ and libgcc floors as Windows PowerShell 5.1 here, so the reader works the same in both.
  - **The AppImage's first launch** took 4.5 s to the window. That is over the PRD's 3 s target in three of its six runs (finding 48).
- **Decided 2026-11-06: keep Ubuntu 22.04 and Debian 12, by linking against an older glibc.**
  - The Package workflow's Linux NativeAOT build now runs in an Ubuntu 22.04 container (glibc 2.35), pinned by digest.
    - The runner's SDK and PowerShell are mounted at their own paths, and the build runs as the runner's user.
    - The job's other steps stay on the runner: the ReadyToRun comparison build, the smoke tests and the packaging.
  - **Why not GitHub's `ubuntu-22.04` runner instead:** GitHub keeps two generally available Ubuntu images. 24.04 and 26.04 both are, so 22.04 is likely to be retired next. A Docker Hub image stays available.
- **CI at `3ed4dc6`** (Package run 42255035086, second attempt; CI run 42255035328 passed): **the floor is glibc 2.34, and the .deb installs and launches on all five releases.**
  - The build container reported glibc `2.35-0ubuntu3.15`.
  - **The floor is glibc 2.34,** set by the bundled GnuTLS stack and now by both executables too. Every program linked against glibc 2.34 or later needs that version, for `__libc_start_main`. `fmod` no longer appears. The libstdc++ floor is unchanged: `GLIBCXX_3.4.29`, from qpdf.
  - **The .deb installed and launched in a bare container of every release it accepts,** with 55 MB on disk each time (window / first page sharp):

| Release | glibc | ICU | First launch | Warm (runs 2–3) |
|---|---|---|---|---|
| Ubuntu 22.04 | 2.35 | 70 | 67 / 142 ms | 62–64 / 138–140 ms |
| Debian 12 | 2.36 | 72 | 66 / 139 ms | 60–65 / 137–138 ms |
| Ubuntu 24.04 | 2.39 | 74 | 131 / 236 ms | 85–98 / 189 ms |
| Debian 13 | 2.41 | 76 | 109 / 200 ms | 79–93 / 168–178 ms |
| Ubuntu 26.04 | 2.43 | 78 | 97 / 186 ms | 69–76 / 142–149 ms |

  - **The network check** passed again on Ubuntu 24.04.
  - **The AppImage** launched on the Ubuntu 26.04 runner, reaching the window in 0.73 s on its first launch. That makes three of its seven runs over 3 s (finding 48). It now needs the same glibc as the .deb, but it is launched only on the runner, so older systems are not checked for it.
  - **Start times did not change noticeably.** The NativeAOT folder on the runner reached the first page in 151–157 ms, against 138–144 ms at `70680f1`, when it was linked on the runner.
  - **The cost is build time:**
    - The container step took 98 s: about 35 s to build the image (clang and its dependencies from Ubuntu's archive) and 63 s to publish.
    - The NativeAOT publish on the runner took 46–69 s in the three runs before.
    - **Caching the image was considered and not done** (2026-11-06). A cached image would still be downloaded and loaded: the clang and LLVM toolchain, several hundred MB by estimate, which takes a good part of the 30–35 s it saves. It would also keep an unpinned toolchain until the cache key changed, and add a cached image that release builds trust.
  - **Two small faults,** fixed at `3d36614`:
    - The publish script's size notice did not become an annotation, because `GITHUB_ACTIONS` was not passed into the container. The log has it: 54.9 MB in 15 files.
    - Reading the container's glibc version printed a harmless "broken pipe" message, because `head` closed the pipe while `docker run` was still writing.
  - **A macOS runner shortage blocked the Linux checks.**
    - In the first attempt, no macOS arm64 runner took the macOS package job, and GitHub cancelled it after 15 minutes.
    - Every install job needs the whole package matrix, so all of them were skipped, the Linux ones included.
    - Re-running the failed jobs passed.
    - **Split at `1744531`:** each OS's package is now its own job, calling a reusable workflow (`package-build.yml`) with its runner and runtime identifier. Each install job waits only for its own OS's package. The steps are unchanged.
    - **First run at `2a4214d`** (Package run 42285696842; CI run 42285696741 passed): every job passed.
      - Each install job started within 2–3 s of its own OS's package finishing. The MSIX job no longer waited for the Linux package, and the DMG job waited 2 minutes for a macOS runner without holding anything else up.
      - The rest matched `3d36614`: glibc 2.34, the .deb on all five releases, and the network check.
      - The AppImage's first launch took 1.6 s to the window, so three of its nine runs were over 3 s (finding 48).
- **CI at `3d36614`** (Package run 42263377962 and CI run 42263377931, both passed at the first attempt):
  - **Both fixes work.** The AOT size is an annotation (54.9 MB in 15 files), and the broken-pipe message is gone.
  - **The same floor,** glibc 2.34. The .deb installed and launched on all five releases again, each reaching the first page in 0.16–0.21 s on its first launch.
  - **The network check** passed again.
  - **The container step took 105 s,** 32 s of it building the image. The other OSes' NativeAOT publishes were slower too: 77–81 s, against 44–65 s in the run before.
  - **The AppImage's first launch** took 1.9 s to the window, so three of its eight runs were over 3 s (finding 48).
  - **The DMG's first launch** took 1.4 s to the window and 3.75 s to the first page, the slowest of both so far (finding 52).

### Findings
50. **NuGet's audit stopped the NativeAOT publish: the pinned runtime has a high-severity vulnerability.**
    - CVE-2026-111497 is an out-of-bounds write in .NET's Brotli decoder, published as an advisory on 2026-11-03. It affects .NET 10 before 10.0.14, which shipped with SDK 10.0.403 on 2026-10-25.
    - The pin is SDK 10.0.402, runtime 10.0.13.
    - **The audit caught it** because a publish restores the ILCompiler package, and NuGet audit warnings are errors.
    - **The CI build does not reach this audit,** because its locked restore has no runtime packages. So the next Package workflow run would fail, and every package built from the current pin ships the vulnerable runtime.
    - This is the monthly SDK bump that finding 14 asks for, made urgent.
    - **Fixed 2026-11-06:** the pin moved to SDK 10.0.403 (runtime 10.0.14), which also fixes CVE-2026-70197, CVE-2026-102870 and CVE-2026-114148.
      - Only `Microsoft.NET.ILLink.Tasks` changed in the lock files.
      - The audit then passed, and so did the NativeAOT publish, all 71 tests, and the smoke test.
      - Local launch times were slower than in S6 (0.26–0.40 s to the window, warm), on a busy machine waiting for a restart after the install. The Package workflow on clean runners is the comparison.
51. **The fuzzer found damaged documents that could have crashed the app at open.**
    - **What happens:** for 23 of the 300 mutated fixtures, PDFium loaded the file but could not read a page's size.
    - **The bug:** the worker threw a generic error, which reached the app as a `WorkerException`. The app's open handler did not catch that type, so the exception would have escaped an async event handler.
    - **Fixed:**
      - The worker now reports such a document as damaged (`Format`), which the app already explains.
      - The app also treats any worker error at open as a file it could not use.
    - **The test guards it:** with the worker fix removed, the fuzz test fails on this case.
    - **Open design question:** other viewers show the pages they can read. Refusing the whole document is the safe choice for now. Showing readable pages is a UX decision for the reader milestones.
52. **Start times on macOS rose over five Package runs, while Windows and Linux held steady. Mostly runner variation.**
    - Window / first page sharp, in ms, on `macos-26-arm64` runners:

    | Commit | DMG, first launch | DMG, warm | NativeAOT folder, warm | ReadyToRun folder, warm |
    |---|---|---|---|---|
    | `81ab077` (S6) | 285 / 1,054 | 109–117 / 286–339 | 151–155 / 409–417 | 318–341 / 724–833 |
    | `63e874f` (S6) | 617 / 2,418 | 158–179 / 475–482 | 151–152 / 400–473 | 317–350 / 693–761 |
    | `97cefbe` | 631 / 2,759 | 179–283 / 442–662 | 230–278 / 564–666 | 532–566 / 1,069–1,183 |
    | `2357e4e` | 739 / 2,909 | 283–383 / 735–814 | 166–171 / 470–485 | 473–731 / 984–1,361 |
    | `70680f1` | 1,008 / 3,527 | 257–286 / 779–781 | 276–336 / 1,855–2,017 | 479–727 / 1,136–1,381 |
    | `6d625fb` (same app code) | 859 / 2,782 | 249–296 / 640–737 | 279–384 / 995–1,258 | 482–1,092 / 1,082–1,809 |
    | `b186887` (same app code) | 1,048 / 3,271 | 206–263 / 574–691 | 135–144 / 345–376 | 334–359 / 671–753 |
    | `3ed4dc6` (same app code) | 1,026 / 3,538 | 402–491 / 1,107–1,743 | 167–182 / 467–471 | 296–332 / 627–749 |
    | `3d36614` (same app code) | 1,442 / 3,754 | 271–275 / 656–764 | 234–332 / 671–760 | 394–607 / 912–1,141 |
    | `2a4214d` (same app code) | 990 / 4,070 | 348–424 / 1,019–1,097 | 434–687 / 1,277–1,440 | 1,778–1,891 / 3,340–4,131 |
    | `42afa16` (search added) | 999 / 3,624 | 387–884 / 795–2,418 | 459–584 / 1,287–1,334 | 732–970 / 2,013–2,258 |

    - **On the other OSes, nothing moved.** At `70680f1`, the NativeAOT folder's warm launches reached the first page in 191–211 ms on Windows and 138–144 ms on Linux, against 196–199 ms and 184–185 ms in S6.
    - **Evidence against a cause in this push:**
      - The only macOS-specific code since S6 is the app's watch on the worker's memory (`2357e4e`), which reads one counter every 250 ms.
      - The NativeAOT folder was four times slower than at `2357e4e`. Yet the DMG holding the same files, launched on another fresh runner, was no slower than at `2357e4e` (779–781 ms against 735–814 ms, warm).
      - The runner image versions do not line up with the times. The slowest folder launches ran on image 20261021.618, the same image as S6's fastest.
      - `6d625fb` changed only Linux packaging, so it rebuilt the same app. Its warm folder launches took 1.0–1.3 s, against 1.9–2.0 s at `70680f1`, and the DMG's first launch took 2.8 s, against 3.5 s. Runner variation alone is that large.
    - **Mostly settled at `b186887`,** a third build of the same app code. Its warm folder launches reached the first page in 0.35–0.38 s, as fast as in S6.
      - So identical code took 0.35, 1.0–1.3 and 1.9–2.0 s warm on these runners: the folder slowdown was the runners, not the code.
      - `3ed4dc6`, a fourth build of the same app code, turned it around: the folder was close to S6 again, while the installed DMG's warm launches were the slowest yet, at 1.1–1.7 s.
    - **The DMG's first launch** reached the first page in 2.6–4.1 s in the twelve runs since S6, the A/B re-runs below included, against 1.05–2.4 s in S6. Its window took 0.6–1.4 s, within the PRD's 3 s target.
    - **A/B re-runs (2026-11-06)** of the Package runs at `97cefbe` (run 42162820338) and `3d36614` (run 42263377962), alternately, twice each:

    | Order | Commit | DMG, first launch | DMG, warm | NativeAOT folder, first run | NativeAOT folder, warm |
    |---|---|---|---|---|---|
    | 1 | `97cefbe` | 1,359 / 4,133 | 369–373 / 944–1,086 | 811 / 3,153 | 281–550 / 895–1,124 |
    | 2 | `3d36614` | 1,074 / 3,690 | 207–324 / 613–785 | 355 / 776 | 168–256 / 466–573 |
    | 3 | `97cefbe` | 785 / 2,634 | 222–234 / 544–727 | 628 / 3,278 | 395–701 / 979–1,418 |
    | 4 | `3d36614` | 920 / 3,680 | 267–398 / 721–941 | 422 / 835 | 154–158 / 407–429 |

      - **No regression since `97cefbe`.** Over three runs each, its DMG first launches reached the first page in 2.6–4.1 s, and `3d36614`'s in 3.7–3.75 s.
        - One commit's first launches spread over 1.5 s. So a single run per commit could never have shown a smaller change.
      - **The published folders were slower at `97cefbe`, in both re-runs:** 3.2–3.3 s to the first page on the first run, against 0.8 s. Its ReadyToRun folder was slower too.
        - The installed DMG holds the same files and showed no such gap, on fresh runners. Nor did `97cefbe`'s original run (564–666 ms warm).
        - So it is probably not the app's code. The macOS build steps on those runners were not slower either, so this comparison does not explain it.
        - **Confirmed at `2a4214d`,** the same app code as `3d36614`: its folder took 2.5 s to the first page on the first run, and its ReadyToRun folder 13.3 s. So slow folder launches happen at the latest code too. They come from the package job's runner, not from `97cefbe`.
    - **Still unexplained: the rise since S6.** Either the runner images drifted, or the cost came in between S6 and `97cefbe`, with the worker operations of `ba0966e` or the SDK bump to 10.0.403.
      - S6's commits cannot be rebuilt as they are: their pinned runtime now fails NuGet's audit (finding 50).
      - S6's app code could be rebuilt with SDK 10.0.403 on a throwaway branch, which would separate the two.
      - A Mac to measure on would settle it.
53. **The .deb accepts older distributions than CI checks, and may not start on them.**
    - Its dependencies accept ICU from Ubuntu 22.04 (`libicu70`) and Debian 12 (`libicu72`), and any version of `libc6`.
    - The NativeAOT executables are linked on the runner, which is Ubuntu 26.04. They can need a newer glibc than the systems the package accepts.
    - They do run on Ubuntu 24.04 (glibc 2.39), where the install job runs. Nothing has been measured below that.
    - If the files need a newer glibc than a system has, apt installs the package and it then fails to start.
    - **Confirmed** at `6d625fb`: the app needs glibc 2.38, above Ubuntu 22.04's 2.35 and Debian 12's 2.36. The floor is now declared, so apt refuses the package there instead of installing one that cannot start (see "Linux releases for the .deb").
    - **The cause** (`b186887`): one function, `fmod`, which the runner's glibc 2.43 binds to its 2.38 version at link time.
    - **Decided 2026-11-06: link against an older glibc**, so both releases stay supported. The options were:
      - **Stop accepting them:** remove `libicu70` and `libicu72` and their install jobs. No cost now, and the .deb supports Ubuntu 24.04 and Debian 13 onwards.
      - **Link against an older glibc:** publish the Linux build in a pinned older image, or with a sysroot, as .NET's own builds do. The floor drops to the bundled libraries' 2.34. It stops depending on the runner image, and the AppImage, whose point is to run on many systems, reaches older ones too. It changes the Linux packaging job, and is an ADR-0005 decision.
      - **Pin `fmod` to its old version** with a linker wrapper: a few lines of C. A stopgap, which the floor annotation would show failing as soon as another function moves.
    - Which distributions to support is a product decision. The PRD asks for the major ones, with a support matrix.
    - **Fixed and verified at `3ed4dc6`:** linked in an Ubuntu 22.04 container, the app needs glibc 2.34, and the .deb installs and launches on Ubuntu 22.04 and Debian 12 (see "Linux releases for the .deb").
54. **The .deb could not be installed on Ubuntu 26.04, the current LTS.**
    - Ubuntu 26.04 ships only ICU 78 (`libicu78`), and the package accepted ICU 70, 72, 74 or 76, so apt would have refused it.
    - From the package lists on packages.ubuntu.com and packages.debian.org (2026-11-06). Debian 13 has `libicu76`, so it was not affected.
    - **Nothing caught it.** The install job ran in an Ubuntu 24.04 container. The AppImage ran on Ubuntu 26.04, but it declares no dependencies.
    - **The fix:** `libicu78` is now accepted. The NativeAOT app already runs with ICU 78: the packaging job's smoke tests run on the Ubuntu 26.04 runner.
    - **Keeping it fixed:** a hand-written ICU list goes stale with each release. So the install jobs now cover every release whose ICU the package accepts, and a comment in `package-linux.ps1` ties the two lists together.

