# Technical Spike Plan — Trustworthy Core v1

**Status:** Planned, not started
**Timebox:** 10 working days
**Owner:** Not yet assigned
**Inputs:**
- PRD v0.2 (`01-PRD-AND-ARCHITECTURE.md`)
- Roadmap (`03-ROADMAP-AND-ACCEPTANCE.md`)
- Viability assessment (`research/2026-10-21-viability-assessment.md`)

## 1. Purpose

The spike answers, with evidence, whether the v1 technology choices hold up before any product code is written:

1. Can Avalonia 12 on .NET 10 render large documents smoothly from an **out-of-process PDFium worker**?
2. Can screen readers read **document text** on Windows, macOS, and Linux?
3. What does **native printing** cost on each OS?
4. Does **qpdf** work from .NET for encryption and structural writes?
5. Is the v1 **redaction method** (rasterise, rewrite, scrub, verify) sound, and can the verifier catch a broken redaction?
6. Can CI produce **installable packages** for all three OSes?

The outcome is a go/no-go decision for each question (§6) and a set of ADRs (§5).

## 2. Rules

- **Timeboxes are hard.** Running out of time is a finding. Record what was learned and the estimated remaining effort.
- **Results must be real.** Spike code may be thrown away, but measurements must not be simulated, and nothing passes without the evidence listed for it (playbook §1, rules 4–5).
- **Record every outcome.** Each item ends as **pass**, **fail**, or **inconclusive**.
- **Inventory every dependency** before using it (checklists §2), even in spike code. Never use excluded dependencies (PRD §7.1).
- **Fixtures** must be synthetic, or public-domain with recorded provenance.
- **Repository name:** use a neutral one for the spike repository (for example `pdf-suite`), not "AlphaPDF", so the conflicting name is not locked in.
- **Priority under time pressure:** cut S3 to Windows-only first, then S6 to unsigned artefacts. Never cut S1, S2, or S5.

## 3. Preconditions

| Item | Needed for | Status / action |
|---|---|---|
| .NET 10 SDK | All | **Not installed** on the development machine. Install it. |
| Public GitHub repository with Actions | S0, S6 | Create it. Standard hosted runners are free for public repositories. |
| Windows development machine | S1–S5 | Available: i5-1235U, ~8 GB RAM |
| macOS | S1, S2 (VoiceOver), S3, S6 | **No Mac available.** Builds and automated checks run on the CI macOS runner. The manual VoiceOver and print-dialog checks need about one day on a borrowed or rented Mac. |
| Linux | S1, S2 (Orca), S3, S6 | A VM (limit it to about 4 GB on this machine), a live USB, or a spare machine, with an X11 session and XWayland available |
| Screen readers | S2 | NVDA (free) and Narrator on Windows; Orca on Linux; VoiceOver on macOS |
| qpdf 12.4.x release binaries | S4, S5 | From the official GitHub releases for the three OSes |
| Apple Developer Program ($99/yr) | S6 notarisation | Optional during the spike. Without it, build an unsigned DMG and record notarisation as untested. |
| Windows signing | S6 | Use a self-signed test certificate for the MSIX during the spike only |

## 4. Spike items

### S0 — Skeleton, CI, and fixtures (Day 1)

**Build:**
- Solution with these projects (names follow PRD §7.4):
  - `PdfSuite.App`: the Avalonia 12 shell.
  - `PdfSuite.Pdf.Abstractions`: the minimal engine interface: open, page count, page size, render tile, extract text with positions, close.
  - `PdfSuite.Pdf.Pdfium`: the PDFium adapter on PDFiumCore and bblanchon.PDFium (non-V8).
  - `PdfSuite.Pdf.Worker`: the out-of-process host.
  - `PdfSuite.Pdf.Qpdf`: used by S4 and S5.
  - One test project.
- GitHub Actions matrix (Windows, macOS, Ubuntu) that restores, builds, and tests.
- A fixture generator that uses only dependencies already in the spike: PDFium for pages, text, images, and attachments, and qpdf (from S4) for metadata. It produces:
  - a 2,000-page mixed document
  - a password-protected document
  - the redaction canary set (S5)

**Pass:** a green build on all three runners, and fixtures that are generated reproducibly from a committed script.

### S1 — Out-of-process tiled rendering (Days 2–4)

**Question:** Does the core viewer architecture perform and fail safely?

**Approach:**
- **Threading:** one thread in the worker process owns every PDFium call. PDFium is not thread-safe.
- **IPC:** a simple length-prefixed message protocol over pipes. Record the choice in ADR-0003.
- **Tiles:**
  - The app requests 256 or 512 px tiles for the visible region, plus a small prefetch margin.
  - Tiles are drawn through Avalonia's custom Skia drawing hook.
  - An LRU tile cache has a memory cap.
  - Requests for tiles that leave the viewport are cancelled.
- **Failure handling:** each request has a timeout. If the worker is killed, the app shows an error state, restarts the worker, and reopens the document.
- **Resource limits:** apply per-OS limits to the worker (Job Objects on Windows, rlimits on Linux and macOS), and record what each OS actually enforces.

**Measure:**
- Time to first page.
- Frame time while scrolling continuously (p50 and p95).
- Longest UI-thread stall.
- Peak memory of the app and of the worker.
- Each measurement at 25%, 100%, 400%, and 800% zoom.
- Avalonia GPU and software rendering modes.
- Windows, macOS, Linux X11, and Linux XWayland.

**Pass** (proposed targets, confirmed on Day 2), on the 2,000-page fixture on the development machine:
- First page in under 2 s.
- p95 frame time of 33 ms or less while scrolling at 100%.
- No UI-thread stall over 100 ms.
- App plus worker memory under 1 GB.
- Killing the worker never crashes the app.
- Builds and renders on all three OSes.

**Fail:** stop and run a 2-day Qt 6 comparison before any further work.

### S2 — Document-text accessibility (Days 5–6)

**Question:** Can a screen-reader user read a page? Avalonia currently has no text pattern (issue #22168).

**Approach:**
- Expose each page as an automation element, with paragraph or line children built from PDFium text extraction, using Avalonia's automation peer API.
- Test with NVDA and Narrator on Windows, Orca on Linux X11, and VoiceOver on macOS.
- Check the status of upstream PR #22178 and its follow-up branches. Estimate the cost of contributing the UIA TextPattern and AT-SPI text work.

**Measure:** for each screen reader, can the user:
- (a) hear the page text?
- (b) move line by line?
- (c) move to the next page?
- (d) find their position after scrolling?

**Pass:** (a) and (b) on all three OSes.

**Partial or fail:** see §6.

### S3 — Native printing (Day 7)

**Question:** What does printing cost on each OS?

**Approach:**
- **Windows:** use the Win32 print dialog. Render pages with PDFium's Windows-only `HDC` rendering, or print rendered bitmaps. `System.Drawing.Printing` is not an option off Windows.
- **macOS:** use `NSPrintOperation` through Objective-C interop, printing page images rendered by the worker. This keeps untrusted parsing inside the worker.
- **Linux:** use the GTK print dialog or the xdg-desktop-portal Print interface, or send rendered output to CUPS.
- **Fallback, recorded for comparison:** hand the file to the OS default handler (the Windows `print` verb, or `lp`) and state this in the UI.

**Measure:**
- Hours spent and lines of native interop on each OS.
- Fidelity of a 50-page mixed document compared with the on-screen rendering.
- Dialog behaviour: printer choice, page range, and cancel.

**Pass:** Windows printing works within the timebox, and macOS and Linux each have either a working proof of concept or an evidence-based estimate.

### S4 — qpdf from .NET (Day 8, morning)

**Question:** Does the structural engine work through P/Invoke on all three OSes?

**Approach:** call qpdf's C API, including its object-handle functions, to:
- encrypt with AES-256 using user and owner passwords and permissions
- decrypt
- write and read Info metadata
- perform a full rewrite that drops unreferenced objects

Record qpdf's crypto provider and its native dependencies (zlib, libjpeg, and the crypto library) in the inventory.

**Pass:**
- An encrypted file opens in PDFium with the correct password and refuses a wrong one.
- Metadata round-trips.
- All of this works in CI on all three OSes.

**Fail:** compare the cost of a PDFBox sidecar with an in-house encryption writer. Encryption may move to v2.

### S5 — Redaction method and verifier (Day 8 afternoon – Day 9)

**Question:** Is the v1 redaction method (PRD §4.7) sound, and does the verifier really catch leaks?

**Approach:**
1. **Canary fixtures:** each fixture carries a unique marker string (for example `CANARY-07`) inside a redaction area, or in a document-level location. The locations cover:
   - text
   - an image (rendered text)
   - vector art
   - a form XObject shared with another page
   - an annotation
   - Info and XMP metadata
   - an attachment
   - a JavaScript action
   - a bookmark title
   - a hidden optional-content layer
   - an earlier incremental revision
2. **Apply:**
   - The worker renders each affected page and fills the areas.
   - A new document is built from those page images, plus the unaffected pages.
   - PDFium saves it non-incrementally.
   - qpdf strips metadata, attachments, scripts, and outlines, and rewrites the file.
3. **Verify:**
   - Decompress the output with qpdf and search the bytes for each canary, in plain, UTF-16BE, and hex-encoded forms.
   - Run PDFium text extraction.
   - Check that the pixels in every redacted area are uniform.
4. **Negative controls:** run the verifier on deliberately broken outputs, which it must flag:
   - an output with only a box drawn over the text
   - an incremental save
   - an output with the metadata left in place
5. **Probe (2 hours):** can PDFium rebuild a text object with the page's original embedded font? Record yes or no. This informs v2 redaction that keeps vector content.

**Pass:** no canary survives in any fixture, and every negative control is flagged.

**Fail:** redaction moves to v2, and v1 ships metadata sanitisation only.

### S6 — Packaging in CI (Day 1 setup, Day 10)

**Question:** Can every platform produce an installable artefact from CI?

**Approach:**
- **Windows:** MSIX, signed with the test certificate.
- **macOS:** a `.app` bundle with the hardened runtime, in a DMG; notarised if Apple membership is available.
- **Linux:** AppImage and .deb.
- Try NativeAOT first. Fall back to a trimmed self-contained build, and record both.
- Place the PDFium and qpdf native libraries per runtime identifier.
- Flathub is out of scope (PRD §5).

**Measure:**
- Package size and cold-start time for each OS.
- Which build mode was used.

**Pass:** each artefact installs on a clean machine or VM and launches the S1 viewer.

## 5. Deliverables

- `docs/research/spike-results.md`: per-item outcome, measurements, effort, and open questions.
- ADRs in `docs/adr/`:

| ADR | Decision | Evidence from |
|---|---|---|
| ADR-0001 | UI framework, .NET version, and printing approach | S1, S2, S3, S6 |
| ADR-0002 | PDF engine strategy: PDFium + qpdf + in-house writer | S1, S4 |
| ADR-0003 | Engine process isolation and IPC | S1 |
| ADR-0004 | Licensing policy and excluded dependencies | Viability assessment §3, inventory |
| ADR-0005 | Packaging and distribution channels | S6 |
| ADR-0006 | v1 redaction method and verification | S5 |

- A dependency and licence inventory for everything the spike used:
  - PDFium and its bundled third-party code: FreeType, ICU, libjpeg-turbo, OpenJPEG, and others
  - PDFiumCore
  - qpdf and its native dependencies
  - Avalonia, SkiaSharp, and HarfBuzzSharp
- The fixture generator and its fixtures, with provenance.
- A CI workflow that passes on all three OSes.

## 6. Go/no-go rules

| Result | Decision |
|---|---|
| S1 and S2 pass | Commit to Avalonia, and begin Milestone 1 |
| S1 fails | Stop. Run a 2-day Qt 6 comparison before any further work |
| S2 passes on Windows only | You decide between two options: (a) v1 ships with a published limitation for document text on macOS and Linux, plus an upstream contribution plan; or (b) run the Qt 6 comparison |
| S2 fails on Windows too | Run the Qt 6 comparison |
| S3: macOS or Linux native printing is too costly | v1 uses the OS default PDF handler on those OSes and says so in the UI. Native printing moves later |
| S4 fails | Compare a PDFBox sidecar with an in-house encryption writer. Encryption may move to v2 |
| S5 fails, or a negative control is not flagged | Redaction moves to v2. v1 ships metadata sanitisation only |
| S6 fails on any OS | That platform is not declared supported until it is fixed (PRD §6) |

## 7. Schedule

| Day | Work |
|---|---|
| 1 | S0 skeleton, CI matrix, and fixture generator; S6 packaging jobs scaffolded |
| 2–4 | S1 tiled rendering, worker isolation, and measurements |
| 5–6 | S2 accessibility (Mac session on Day 6 for VoiceOver) |
| 7 | S3 printing |
| 8 | S4 qpdf (morning); S5 begins |
| 9 | S5 redaction and verifier |
| 10 | S6 packaging; ADRs; spike report |

This schedule is tight for one developer working with AI agents. S4 and the S6 CI work don't touch the UI, so they can run in parallel with S1–S3 if a second person or agent is available.

## 8. Out of scope

- Annotations, the search UI, forms, OCR, signatures, the CLI, tabs, settings, and visual polish.
- Microsoft Store submission and Flathub.
- Production-quality error handling beyond what S1 measures.

## 9. Open decisions that do not block the spike

- **Team size:** sets the milestone durations in the roadmap.
- **New product name:** needed before any public release. Use the neutral repository name until then.
- **AI-usage disclosure wording:** the spike repository is public from Day 1 for free CI, so publish a short interim disclosure when it is created. The full policy is a Milestone 0 deliverable.
- **Apple Developer Program:** when to join. It is required for notarisation and for Homebrew distribution.
- **Windows signing route:** Microsoft Store only, SignPath Foundation after a first release, or an OV certificate.
