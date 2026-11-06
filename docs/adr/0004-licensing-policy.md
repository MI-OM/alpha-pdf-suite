# ADR-0004: Licensing policy and excluded dependencies

**Status:** Proposed (2026-11-01). Accepting it also adopts Apache-2.0 for the project's own code, which the README still lists as not adopted. That is the maintainer's decision.
**Decides:** the licence of project code, which dependency licences are allowed and on what terms, and the rules that keep that true over time.

## 1. Context and constraints
- **Licence:** PRD §10 proposes Apache-2.0 for project code.
- **What the project ships:**
  - native binaries: PDFium, qpdf, Skia, HarfBuzz, ANGLE, and the .NET runtime compiled in by NativeAOT
  - packaging runtimes: the AppImage runtime
  - later, possibly OCR data, fonts and codecs
- **Licences change under dependencies:** PDFsharp moved from MIT to AGPL-3.0 in 6.5.0 (2026-10-14).
- **Avalonia's tooling and premium components became commercial** in October 2025. Its core stays MIT.
- **Distribution channels and funders have their own rules:** Flathub requires human-authored manifests and disclosure of AI-generated code; NLnet does not fund work produced with generative AI (PRD §10).
- This is engineering record-keeping, not legal advice.

## 2. Options considered
**For project code:**
- **Apache-2.0:** permissive, with an explicit patent grant. Compatible with every permissive dependency, and with LGPL libraries used as replaceable libraries.
- **MIT:** permissive, but no patent grant.
- **MPL-2.0:** file-level copyleft. It would add obligations for contributors and downstream users, for no gain the project needs.
- **GPL or AGPL:** would allow Poppler or MuPDF, but rules out permissive reuse, and conflicts with the product's goal of a permissive core.

**For dependencies:**
- **Permissive only:** the simplest, but it would rule out qpdf's Linux archive and the AppImage runtime as shipped.
- **Permissive, plus weak copyleft under conditions:** the proposal below.
- **Case by case, with no policy:** rejected; it does not survive licence changes or many contributors.

## 3. Evidence collected
- **Viability assessment §3.1:** the licence filter. PDFium, qpdf, PdfPig and PDFiumCore pass. MuPDF, iText, Ghostscript, Poppler and PDFsharp 6.5.0 or later fail.
- **The dependency inventory (`docs/licensing/dependency-inventory.md`), covering everything the spike uses:**
  - **Permissive:** MIT, BSD, Apache-2.0, zlib and IJG, for all NuGet packages and PDFium.
  - **Weak copyleft as shipped:**
    - qpdf's Linux archive: GnuTLS, Nettle and others, as separate shared libraries (finding 26)
    - the AppImage runtime: libfuse, linked statically (finding 45)
  - **Redistributable under the vendor's terms:** the Microsoft C++ runtime, in qpdf's Windows archive.
  - **Missing notices:** PDFium's NuGet packages ship none (finding 4). The .NET runtime's notices must ship with NativeAOT executables.
  - **Telemetry found** in Avalonia.BuildServices and the test platform. Both are disabled and verified (finding 3; inventory §3).
- **Supply chain:**
  - Package versions are pinned centrally, with lock files and locked restores in CI.
  - Native binaries from outside NuGet are pinned by SHA-256: qpdf, appimagetool, and the AppImage runtime.
  - GitHub Actions are pinned to commit SHAs (since 2026-11-06).
  - Publishes restore packages that no lock file covers (finding 42; ADR-0005).

## 4. Decision and rationale
**Proposed:**
1. **Project code is licensed Apache-2.0.**
2. **Allowed without special review** (after inventory): permissive licences such as MIT, BSD, Apache-2.0, ISC, zlib and IJG.
3. **Allowed with recorded obligations** (weak copyleft, such as LGPL or MPL): only where the component stays separately replaceable, as a shared library, a separate process, or a self-contained runtime with its source available. Each one needs:
   - an inventory row naming the obligations
   - notices and a source offer in the release
4. **Not allowed** without a new licensing ADR:
   - strong copyleft (GPL, AGPL): MuPDF, iText (including pdfSweep), Ghostscript, Poppler, and PDFsharp/MigraDoc 6.5.0 and later
   - commercial components: paid Avalonia components and tooling, including Parcel and the paid PDF viewer
   - unmaintained bindings: Docnet.Core, PdfiumViewer
5. **Rules that keep this true:**
   - Inventory every dependency before first use, even in spike code.
   - Re-check its licence on every version bump.
   - Allow no dependency that sends telemetry, unless the telemetry is disabled at repository level and the disabling is verified.
   - Generate and review an SBOM before every release.
   - Packaging manifests for Flathub are human-authored (playbook rule).

**Rationale:**
- Apache-2.0 matches every engine the project relies on, and the patent grant matters for a document-format product.
- The weak-copyleft rule admits exactly what the spike found necessary, on terms that are standard to meet.

## 5. Trade-offs
- **Excluded engines:** some capable engines (MuPDF, Poppler) stay out, so features they would provide must be built or found elsewhere.
- **Release work:** LGPL components bring notices and source offers. The alternative is building qpdf with native crypto only, or not shipping an AppImage.
- **Inventory upkeep:** inventory discipline costs time on every dependency change.

## 6. Licensing and security consequences
- **Open items before any release** (inventory §4):
  - PDFium's and its bundled libraries' notices
  - SkiaSharp and HarfBuzzSharp notices
  - the .NET runtime notices
  - the AppImage LGPL obligations
  - reviewer and approval dates for every inventory row
- **Security:** the telemetry rule and the pinning rules are supply-chain controls as much as licence ones. GitHub Actions are pinned to commit SHAs; locking publish restores remains open.

## 7. Reversibility and migration cost
- **The project licence:** changing it is cheap before outside contributions arrive, and needs every contributor's consent afterwards. That is why it should be decided before the repository takes contributions.
- **The dependency policy:** a project document; it can be tightened or relaxed at any time with a superseding ADR.

## 8. Validation experiment
- **Before the first release:**
  - generate an SBOM from the lock files and native manifests, and compare it with the inventory
  - add a licence check to CI that fails on a dependency missing from the inventory
  - assemble the third-party notices file for each package format

## 9. Conditions that would cause reconsideration
- **A dependency changes its licence,** as PDFsharp did.
- **A distribution channel or funder** requires terms this policy does not meet.
- **A needed capability exists only** under an excluded licence and cannot reasonably be built.
- **Contributors or a foundation the project joins** require a different licence or a contributor agreement.
