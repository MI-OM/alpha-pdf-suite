# ADR-0002: PDF engine strategy: PDFium, qpdf, and an in-house writer later

**Status:** Accepted (2026-11-11), by the agent at the maintainer's instruction to take decisions; the maintainer can reverse it (decisions for review, Q4).
**Decides:** which libraries read, render, edit and write PDFs, how they are bound, and which gaps are left for project code.

## 1. Context and constraints
- **What v1 needs** (PRD §4):
  - rendering, text extraction and search
  - annotations, page operations and form filling
  - AES-256 encryption and metadata writes
  - a full-rewrite cleanup for redaction
- **Licence:** the project licence is Apache-2.0 (adopted 2026-11-11, ADR-0004), so AGPL and GPL engines are excluded.
- **No permissive .NET writer:** since PDFsharp moved to AGPL in 6.5.0 (2026-10-14), no maintained, permissively licensed .NET library writes and manipulates PDFs.
- **PDFium's own limits:** it cannot write encryption, its public API is not thread-safe, and the prebuilt binaries lack PartitionAlloc hardening (viability assessment §3.3).

## 2. Options considered
- **A. PDFium, plus qpdf through its C API, plus a small in-house writer only when a [v2] feature needs one.** All three are permissive.
- **B. PDFium with PdfPig.** PdfPig (Apache-2.0) reads and analyses layout, but writes neither encryption nor annotation edits.
- **C. A PDFBox sidecar** (Apache-2.0, Java). Capable, but it needs a Java runtime or IKVM.
- **D. A frozen fork of PDFsharp 6.2.4** (MIT). Unmaintained by definition, so the project would own the whole writer.
- **Excluded by licence:** MuPDF, iText (including pdfSweep), Ghostscript, and Poppler (ADR-0004).

**How to bind PDFium:**
- **PDFiumCore:** trails the native packages, and its obsolete `4688.0.0` release sorts as the latest.
- **Project-owned `LibraryImport` bindings** to the bblanchon non-V8 binaries.

## 3. Evidence collected
- **PDFium:**
  - It rendered, extracted text and printed in S1–S3.
  - It authored every fixture, after chunking around quadratic content generation (finding 1).
  - It copied pages during the redaction rebuild without carrying references to pages that were not copied (finding 33).
  - Rendering differs slightly between OSes; written output does not (finding 15).
  - It assigns random file IDs and creation dates, with no API to set them (finding 2).
  - The prebuilt binaries do not export GDI text printing (finding 21).
  - The NuGet packages ship no licence or notice files (finding 4).
- **qpdf 12.4.2 (S4):**
  - AES-256 encryption and decryption, Unicode metadata, and full rewrites all work on all three OSes. PDFium checked the results as an independent reader (CI run 41327508609).
  - Metadata must go through the object API (finding 27).
  - Some C functions throw C++ exceptions, which end the process on Linux and macOS (finding 28).
  - It provided the redaction scrub, and the verifier's object walk (S5).
- **Bindings:**
  - The project-owned bindings compile with NativeAOT and no warnings (finding 38).
  - The PDFium project (bindings, adapter and authoring API) is about 1,430 lines; the qpdf project about 810.
- **Text forms:** PDFium writes text as hex strings, so byte searches need care (finding 29).
- **v2 redaction:** the glyph-level probe works through PDFium for fonts that map back from Unicode (finding 36).

## 4. Decision and rationale
**Proposed:**
1. **PDFium** for rendering, text, search, annotations, page operations, form filling and page-object editing.
   - Bound through project-owned `LibraryImport` bindings to bblanchon's non-V8 prebuilt binaries, now chromium/8111.
   - The engine stays behind `IPdfEngine` and `IPdfDocument`.
2. **qpdf** through its C API, including object handles, for encryption, metadata, structural cleanup and redaction rewrites.
   - The official release binaries are pinned and SHA-256-verified (`tools/qpdf/fetch-qpdf.ps1`).
3. **Both run only in the worker process** (ADR-0003).
4. **No in-house writer until a [v2] feature needs one,** such as signing or Redact annotations.
5. **PdfPig** remains a candidate for layout analysis and reading order (finding 16). It is not adopted by this ADR.

**Rationale:**
- Every v1 capability is covered by two permissive, actively maintained native libraries that work from .NET on all three OSes.
- Owning the bindings keeps versions aligned with the native packages, and keeps NativeAOT working.

## 5. Trade-offs
- **Two object models** (PDFium's and qpdf's): data passes between them as serialised PDF bytes.
- **Prebuilt PDFium** has no PartitionAlloc and no GDI text printing. Building PDFium ourselves would fix both, and Flathub requires it, but it means a Chromium-toolchain build in CI.
- **qpdf's C++ exceptions** must be avoided by how it is called, and contained by the worker.
- **The bindings and adapters** are maintained by the project: the two engine projects are about 2,200 lines today.

## 6. Licensing and security consequences
- **PDFium:** BSD-3-Clause or Apache-2.0. Its notices, and those of the libraries it bundles, must come from the matching upstream release. That is a release blocker (inventory §4).
- **qpdf:** Apache-2.0.
  - On Windows it links OpenSSL statically and needs the MSVC runtime.
  - On Linux its archive bundles LGPL libraries, GnuTLS among them (finding 26). Building qpdf with only its native crypto provider would remove them.
- **Security:** PDFium had about 24 CVEs published in 2026, mostly High (viability assessment §3.3). So:
  - the worker isolation is mandatory
  - PDFium updates ship at least monthly, and out of band for High or Critical CVEs (PRD §8)

## 7. Reversibility and migration cost
- **Where engine code lives:** callers see the engine only through `PdfSuite.Pdf.Abstractions` (about 90 lines) and the worker protocol.
- **Replacing PDFium** means a new adapter behind the same interfaces, plus re-baselining rendering tests.
- **Replacing qpdf** touches `PdfSuite.Pdf.Qpdf` and the redaction scrub.
- **Neither engine's types leak into the app.**

## 8. Validation experiment
- **Done in the spike:** S1, S3, S4 and S5 in CI on all three OSes.
- **Not done yet:**
  - continuous, coverage-guided fuzzing of the parse and transform paths (PRD §8). A seeded test of 300 mutated documents runs in CI (spike finding 51)
  - a first monthly PDFium bump done as a routine, with the lock files and fixtures
  - an experiment building PDFium from source (decides GDI text, PartitionAlloc and Flathub together)
  - a qpdf build with native crypto only, for Linux

## 9. Conditions that would cause reconsideration
- **PDFium or qpdf changes licence,** or bblanchon's binaries stop shipping.
- **The PDFium CVE rate or patch lag** makes prebuilt binaries untenable without our own build.
- **A v1 feature turns out to need writing** that neither engine offers. That brings the in-house writer forward.
- **A maintained, permissively licensed .NET PDF writer appears.**
