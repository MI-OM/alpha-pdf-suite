# ADR-0001: UI framework, .NET version, and printing approach

**Status:** Proposed (2026-11-01). Acceptance waits on the manual screen-reader checks of spike item S2. Spike plan §6 commits to Avalonia only when S1 and S2 both pass.
**Decides:** the desktop UI framework and runtime, how document text reaches screen readers, and how each OS prints.

## 1. Context and constraints
- **The PRD's requirements:**
  - one C# codebase for Windows, macOS and Linux (§6)
  - smooth viewing of a 2,000-page document at 25–800% zoom, with first page in under 2 s and a usable window in under 3 s (§11)
  - screen-reader access to document text, not only to the controls (§4.9)
  - native printing (§6)
  - offline use with no telemetry (§9)
- **The baseline proposed by the viability assessment:** Avalonia 12 on .NET 10 LTS, with Qt 6 as the fallback (PRD §7.1).
- **Known gaps in Avalonia:**
  - no automation text pattern: issue #22168 is open, and PR #22178 only defines the shared contract
  - no printing API
- **Licence constraint:** only Avalonia's MIT core packages may be used. Its developer tools, Parcel packager and premium components (including a paid PDF viewer) are commercial.
- **.NET support dates:** .NET 8 and 9 reach end of support on 2026-11-10. .NET 10 LTS is supported until 2028-11-14.

## 2. Options considered
**UI framework:**
- **A. Avalonia 12 on .NET 10, MIT packages only.** Keeps C#; rendering through Skia with a custom drawing hook.
- **B. Qt 6** (LGPL, C++ or PySide). The lowest accessibility and printing risk, but a second language and LGPL obligations. Qt PDF is itself built on PDFium.
- **C. Electron with PDF.js.** Mature accessibility and printing, but large installs and a JavaScript engine in the app.
- **D. Tauri.** Weaker printing and accessibility on Linux and macOS.
- **Not viable:** Uno and .NET MAUI, which do not meet the Linux requirements.

**Document text for screen readers:**
- **E. Keyboard focus:** a focusable element per line, grouped by page. It uses only focus events, which every screen reader on every OS supports.
- **F. Wait for, or contribute, Avalonia's text pattern** (UIA TextPattern, AT-SPI Text, and NSAccessibility, for which nothing is planned upstream).

**Printing**, for each OS:
- native dialog and printing
- handing the file to the OS default handler
- rendering bitmaps and sending them to CUPS

## 3. Evidence collected
- **Rendering (S1, Windows; spike results, S1 measurements):** every target met on the 2,000-page fixture.
  - first page sharp in 0.43–0.84 s
  - p95 frame interval of 17.2–17.4 ms (GPU) at every zoom level
  - worst frame 34–51 ms
  - app plus worker at most 641 MB
  - recovery from a killed worker in 200 ms
  - **Caveat:** software rendering is just inside budget, at a p95 of 31.2–31.5 ms (finding 10).
  - **Not measured yet:** interactive runs on Linux and macOS. The pipeline tests pass there, and the S6 packages render the first page on all three OSes.
- **Document text (S2):** design E is built.
  - UI Automation shows one focus event per line, across pages, and the expected names for pages with no text.
  - An A/B benchmark found no frame-pacing cost (finding 19).
  - **Not done:** real screen-reader checks with NVDA and Narrator are pending with the maintainer. Orca and VoiceOver are untested.
  - **Known limits of design E:** no continuous "say all", no character navigation through the document, and no keyboard selection (finding 17).
  - Reading order follows the content stream (finding 16).
- **Printing (S3):**
  - **Windows works:** the Win32 dialog in the app, and GDI drawing in the worker. That took about 300 lines of interop and half a day. Output has exact geometry, about 1 s per page, and text sent as outlines (finding 21).
  - **macOS and Linux are researched, not built.** Estimates: 2–3 days for Linux (xdg-desktop-portal Print, with CUPS as the fallback); 2 days for macOS (libcups and the app's own dialog), or 5 with the native panel. Confidence is low to medium.
- **Startup and packaging (S6):**
  - NativeAOT builds the Avalonia app with no warnings (finding 38).
  - Installed packages reach the window in 0.07–0.62 s on a first launch, on all three OSes (findings 39 and 49).
- **Runtime pinning:** a .NET patch release broke CI with no code change, so the SDK is pinned exactly (finding 14).

## 4. Decision and rationale
**Proposed:**
1. **Framework:** Avalonia 12.1.x on .NET 10 LTS, using MIT packages only. The SDK stays pinned exactly in `global.json` and is bumped monthly, after Patch Tuesday.
2. **Document text:** expose it through focus-based reading elements per page and line (design E), until a text pattern exists upstream. Track #22178 and contribute to it where possible. Reading order needs its own decision before Milestone 1 (finding 16).
3. **Printing:**
   - **Windows:** native: the Win32 dialog in the app, and the worker renders.
   - **Linux:** xdg-desktop-portal Print, with CUPS `lp` where the portal is missing.
   - **macOS:** libcups with the app's own dialog, then the native panel.
   - Each route needs a 1-day proof of concept on real hardware. Where a route is not ready by the v1 cut, v1 hands the file to the OS default PDF handler there and says so in the UI (spike plan §6, S3 rule).

**Rationale:**
- Avalonia met every rendering target it was measured against, compiles to NativeAOT, and keeps one C# codebase with the engine, worker and redaction code.
- Its two gaps have working routes:
  - focus-based reading needs no upstream change
  - printing is per-OS interop of measured or estimated size
- Qt 6 would remove those gaps, but at the cost of a second language, LGPL obligations, and rewriting about 1,800 lines of UI.

## 5. Trade-offs
- **Reading:** focus-based reading is a workaround. A full text pattern would add say-all, character and word navigation, and selection.
- **Printing:** it is per-OS native interop and must be maintained three times. On Windows, text prints as outlines unless the project builds PDFium itself (finding 21).
- **Avalonia's free tier** is a "goodwill offering" whose terms can change. Only MIT core packages are used, so a change would affect tooling, not shipped code.
- **Linux sessions:** Wayland runs through XWayland. X11 is the tested session (PRD §6).
- **Runtime patches:** with the SDK pinned, .NET security patches arrive only when the pin is bumped.

## 6. Licensing and security consequences
- **Licences:**
  - Avalonia, SkiaSharp and HarfBuzzSharp: MIT
  - ANGLE: BSD-3-Clause
  - .NET runtime: MIT, compiled into the executables
  - All are recorded in the inventory.
- **Telemetry:** Avalonia's build telemetry is disabled at repository level (inventory §3).
- **The app never parses a document,** including for printing: the worker renders, and on macOS and Linux the OS print system renders a copy of the file.
- **Printing reaches the print spooler from the worker.** A future OS sandbox for the worker must allow that, or printing moves to a small broker process (finding 25; ADR-0003).

## 7. Reversibility and migration cost
- **Where the UI lives:** only in `PdfSuite.App`, about 1,800 lines.
- **What a framework change would keep:** the engine adapters, the worker, its binary protocol, and redaction have no UI dependency, and stay as they are.
- **What moving to Qt 6 means:** rewriting the shell, the tiled view and the reading elements in C++ or Python, and talking to the worker over the same protocol. Roughly 2–4 weeks, before printing and accessibility work. That is an estimate, not a measurement.

## 8. Validation experiment
- **Must pass before acceptance:** the S2 checklist with NVDA and Narrator (spike results, S2 manual checklist). Pass means steps 2–5 work in both readers.
- **Before macOS and Linux are declared supported:**
  - Orca on Linux X11, and VoiceOver on a Mac
  - the S1 interactive benchmark on Linux (X11 and XWayland) and macOS
  - the printing proofs of concept
  - display scaling above 1.0

## 9. Conditions that would cause reconsideration
- **NVDA or Narrator cannot read the text line by line:** run the Qt 6 comparison (spike plan §6).
- **S2 passes on Windows only:** the maintainer chooses between a published limitation for macOS and Linux, with an upstream contribution plan, and the Qt 6 comparison.
- **S1 fails on Linux or macOS,** for example software rendering over budget on common hardware.
- **Avalonia's core packages change licence,** or the core stops being maintained.
- **User testing shows focus-based reading is not usable in practice,** and no text pattern is in sight upstream.
