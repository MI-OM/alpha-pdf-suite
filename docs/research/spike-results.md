# Spike Results

Results of the technical spike in `docs/06-SPIKE-PLAN.md`. Each item records its outcome, measurements, and findings. Measurements are from the development machine (Intel i5-1235U, about 8 GB RAM, Windows 11 x64) unless stated otherwise.

| Item | Status | Outcome |
|---|---|---|
| S0 Skeleton, CI, fixtures | Done locally; CI not yet run | Pass on Windows. Linux and macOS need a first CI run |
| S1 Tiled rendering | Not started | — |
| S2 Document-text accessibility | Not started | — |
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

### Not yet covered by S0
- **Password-protected, metadata, JavaScript, bookmark, hidden-layer, and vector-art canary fixtures.** They need qpdf or raw object writing, and arrive with S4 and S5.
- **CI on Linux and macOS.** The workflow is written but has not run.
