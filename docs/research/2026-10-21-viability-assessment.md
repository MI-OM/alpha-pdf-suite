# Viability Assessment — Open-Source PDF Suite ("Alpha PDF Suite")

**Date:** 2026-10-21
**Status:** Research complete; decisions pending (see §9)
**Scope:** Market, PDF engine and licensing, UI framework, hard features, distribution/cost/sustainability, naming, local environment
**Method:** Five parallel desk-research streams using primary sources (official repos, header files, licence files, pricing and policy pages, advisories, papers), followed by direct re-verification of the claims that most affect the decision (marked ✔ below).

**Confidence key:** H = verified on a primary source · M = secondary source or partially verified · L = inference/judgement

This is engineering research, not legal advice.

---

## 1. Verdict

| Question | Answer |
|---|---|
| Is it **technically** buildable under Apache 2.0? | **Yes.** Every layer has a permissively licensed building block — but several headline features have *no* permissive implementation anywhere and must be built in-house. |
| Is the **PRD as written** achievable in ~12 months by a small team / one person with AI agents? | **No.** True redaction, existing-text editing, full signature validation, PDF/UA remediation and XFA are multi-year efforts even for funded commercial teams. |
| Is there still a **market gap**? | **Yes, but it is narrowing fast.** At least two projects launched in 2026 target nearly the same scope, one of them under MIT/Apache. |
| Is the **name** usable? | **No.** "AlphaPDF" is an active commercial product. |
| Recommended path | **Rescope before coding:** pick a differentiated angle (§5), adopt the revised v1 in §6, and run the 2-week go/no-go spike in §8. |

The project's own principles (honest feature matrix, acceptance-tested claims, verified redaction, no misleading trust indicators) are its strongest asset. The 2026 field is filling up with fast-moving "open Acrobat" clones; a project that is *demonstrably trustworthy* on a smaller feature set is the most defensible position.

---

## 2. Market and competition

### 2.1 Landscape (as of 2026-10-21)

| Project | Licence | Win / Mac / Linux | What it covers | Health | Conf. |
|---|---|---|---|---|---|
| **PdfCraft** (storytold) | MIT OR Apache-2.0 | ✔ / ✔ / ✔ (+FreeBSD, web) | Self-reported: viewing, page ops, comments, form fill/authoring, redaction, basic signatures, printing, a11y checker, CLI/MCP. Partial: text editing, non-Latin OCR. Missing: Office conversion, LTV, PDF/UA preflight, tagging, XFA | **Alpha**, launched Oct 2026; ~16.9k stars; Rust/egui; AGENTS.md + CLAUDE.md in repo | H ✔ |
| **Open PDF Studio** (OpenAEC Foundation) | LGPL-3.0 | ✔ / ✔ / ✔ (+Android) | Annotations, page ops, AcroForm + XFA fill, field creation, desktop OCR, inline text editing, redaction, signature validation | ~1,460 commits, 937 stars; Tauri 2 + PDFium | H ✔ |
| **Stirling-PDF** (incl. desktop app) | MIT core; desktop and proprietary directories under separate licences; © Stirling PDF Inc. | ✔ / ✔ / ✔ | 60+ tools, text editor, redaction, forms. **Desktop "Local Only" mode cannot run OCR, Office conversion or PDF/A conversion** — those need Stirling Cloud or a self-hosted server | Very active; ~94.7k stars; $2M VC (open-core) | H ✔ |
| **PDF4QT** | **MIT** (relicensed from LGPL on 2025-04-27) | ✔ / ✗ / ✔ | Annotations, forms, signing + validation, Tesseract OCR, redaction, page manager, CLI | One maintainer (250 of 262 commits in the last 12 months) | H ✔ |
| Okular | GPL-2.0+ | Store / nightly only / ✔ | Annotations, signatures; no page ops, OCR or redaction | KDE, active | H |
| SumatraPDF | GPLv3 | ✔ / ✗ / ✗ | Reader; 3.7 pre-release adds editing, redaction, signing | One maintainer | H |
| Xournal++, PDF Arranger, GNOME Papers, PDFsam | GPL / AGPL | Mixed | Single-purpose (handwriting, page ops, viewing) | Active | H |
| BentoPDF | AGPL-3.0 + commercial | Browser only | Very broad feature set | Active | H |
| Browser viewers (Firefox PDF.js, Chrome, Edge), macOS Preview | — | — | Now include annotation, signing, page reorder; Preview has redaction | — | H/M |

### 2.2 What is still missing

No **mature, stable** project combines all of: permissive licence + fully local processing + native desktop + all three OSes + broad features. Thin areas across *every* project:

- Fully offline OCR in a polished GUI on **macOS** (macOS is the weakest OS for OSS PDF apps overall).
- Reliable existing-text editing, especially CJK and right-to-left.
- Accessibility tagging/remediation and PDF/A–PDF/UA preflight in a GUI.
- Long-term signature validation (LTV).
- A stable, scriptable CLI/SDK that matches the GUI.

### 2.3 Fork, contribute, or start fresh?

- GPL/AGPL/LGPL projects (Okular, SumatraPDF, Xournal++, Open PDF Studio, BentoPDF) **cannot be relicensed** to Apache 2.0.
- Stirling's desktop UI is outside its MIT grant.
- **PdfCraft (MIT/Apache) and PDF4QT (MIT) are licence-compatible.** PdfCraft is the closest match in goal, licence and platforms. Its risks are youth, unproven hardening, and unknown governance and funding.
- A fresh Avalonia/.NET project is justified **only with a distinct angle** (§5). Otherwise it becomes the fourth or fifth "open Acrobat" launched in 2026.

**Demand signals:** Stirling ~94.7k stars and 6.2M Docker pulls; PdfCraft ~17k stars in about three weeks; long-running Linux forum complaints about the lack of an Acrobat-class editor. Demand is real, and so is the competition for it. (H/M)

---

## 3. PDF engine and licensing

### 3.1 Licence filter

| Library | Licence | Usable in an Apache-2.0 app? | Conf. |
|---|---|---|---|
| PDFium | BSD-3 / Apache-2.0 | **Yes** | H |
| qpdf | Apache-2.0 | **Yes** (C API via P/Invoke; no maintained .NET binding) | H |
| PdfPig | Apache-2.0 | **Yes** (reading and layout analysis; no encryption writing, no annotation edits) | H |
| PDFiumCore / PDFtoImage | Apache-2.0 / MIT | **Yes** | H |
| Apache PDFBox | Apache-2.0 | Yes, but Java (sidecar process or IKVM) | M |
| **PDFsharp / MigraDoc** | **AGPL-3.0 + commercial since 6.5.0 (2026-10-14)**; ≤ 6.2.4 MIT | **Only a frozen 6.2.4 fork** | H ✔ |
| MuPDF | AGPL-3.0 / commercial | **No** | H |
| iText (incl. pdfSweep) | AGPL-3.0 / commercial | **No** | H |
| Poppler | GPL-2.0 | **No** | H |
| Ghostscript | AGPL / commercial | **No** | M |

> **New risk:** PDFsharp's licence change one week ago means the .NET ecosystem no longer has a maintained, permissively licensed PDF *writing and manipulation* library. This is both a hazard (dependency licences can change under you, so pin versions and record them in the inventory) and a possible **opportunity** (§5, option B2).

### 3.2 What PDFium can and cannot do (main branch, 2026-10-20)

**Can:**
- Render pages.
- Extract per-character text and positions; search.
- Create 13 annotation types and edit annotation dictionaries.
- Page operations: new, delete, move, import, rotate, N-up.
- Insert, remove and transform page objects, including inside form XObjects.
- Set text, charcodes, positions and render mode. Invisible render mode makes OCR text layers feasible.
- Replace image bitmaps and regenerate content streams.
- Incremental or full save. A full save writes only referenced objects, so removed content is dropped.
- Read signature data: contents, ByteRange, SubFilter, DocMDP.
- Fill AcroForms through the event-style `FORM_*` API. (H)

**Cannot:**
- **Redact.** Redact annotations are rejected.
- **Write or change encryption.**
- **Sign or validate signatures.**
- Write outlines, Info/XMP metadata, page labels, the structure tree or new form fields.
- Create Line, Polygon, PolyLine or Caret annotations.
- Linearise output.

Also note:
- **No PDFium API is thread-safe.**
- All annotation and signature functions, and about half of the editing functions, are marked "Experimental API". (H)

**Consequence:** PDFium alone is not enough. The minimum viable engine stack is:

1. **PDFium** via PDFiumCore and bblanchon's weekly prebuilt binaries for win/mac/linux × x64/arm64. Run it in an **out-of-process worker** with a single-threaded queue.
2. **qpdf** as a second, "structural" adapter: AES-256 encryption, password changes, linearisation, metadata, outlines and page labels, and post-redaction garbage collection.
3. An **in-house C# low-level object writer and incremental-update writer**. It is needed for signing, Redact annotations and form-field creation, because neither PDFium nor qpdf can append incrementally with new structures.

### 3.3 Security and patch burden

- An NVD keyword search returns about **24 PDFium CVEs published in 2026**, almost all rated High, against 1–3 per year in 2022–2025. These are keyword counts, so treat them as approximate. (M)
- bblanchon's pdfium-binaries ships **weekly**.
- **Every build sets `pdf_use_partition_alloc=false`**, so it lacks Chrome's allocator hardening. Chrome also runs PDFium inside a sandbox; an in-process desktop app would not. (H)
- PDFium is **not** in OSS-Fuzz. It has about 45 in-tree fuzz targets that Chromium runs. (M)

**Implications:**
- The PRD's "use a process boundary where feasible" should become **mandatory** for PDFium.
- Budget at least a monthly PDFium bump, plus out-of-band releases when a High CVE lands.

---

## 4. UI framework (Avalonia / .NET)

| Area | Finding | Conf. |
|---|---|---|
| Version and licence | Avalonia 12.1.4 (2026-10-09), core **MIT**. Since Oct 2025, dev tools, the Parcel packager and premium components (including a paid PDF viewer) are commercial. The free tier is a "goodwill offering" whose terms can change. **Use only MIT core packages; package the app with your own scripts.** | H |
| .NET target | .NET 8 and 9 reach end of support on **2026-11-10**. **Target .NET 10 LTS** (supported to 2028-11-14). | H |
| Accessibility | Windows UIA, macOS NSAccessibility and Linux AT-SPI2 exist. AT-SPI2 is new in 12.0 and X11-only. **Issue #22168 (open, 2026-09-07): Avalonia's automation tree has "no text pattern at all"**, so Narrator cannot read documents and Orca cannot navigate text. A fix (PR #22178) awaits API review. **No macOS text-accessibility work is listed.** | H ✔ |
| Printing | **No printing API.** Third-party packages are immature. Expect native interop for each OS: Win32, NSPrintOperation/PDFKit, GTK/portal/CUPS. | H |
| Rendering | Skia/GPU with a supported custom-draw hook, which suits tiled page rendering. **No published benchmarks** for large-document viewers. Existing Avalonia PDF viewers are alpha or image-only. | M |
| Packaging | NativeAOT is supported but cannot cross-compile, so you need Windows, macOS and Linux CI runners. macOS bundling and notarisation are documented. Linux docs cover .deb; Flatpak works via the .NET SDK extension; Wayland support is early. | H |

**Alternatives:**
- **Qt 6** has the lowest accessibility and printing risk. It brings LGPL obligations and a C++ or PySide codebase. Qt PDF is itself built on PDFium.
- **Electron + PDF.js** has mature accessibility and printing, but large installs.
- **Tauri** is weaker on Linux and macOS printing and accessibility.
- **Uno** and **.NET MAUI** fail the Linux requirements.

**Recommendation:** keep Avalonia if the team is committed to C#, **conditional on the spike in §8.** Screen-reader access to *document text* (which the PRD requires) is real engineering work: either a custom automation peer per OS or contributing the text pattern upstream. If v1 needs full screen-reader support on all three OSes and C# is not a hard requirement, Qt 6 is the lower-risk choice.

---

## 5. Strategic options

| Option | Description | Pros | Cons |
|---|---|---|---|
| **A. Contribute** | Put effort into PdfCraft (licence-compatible), or a macOS port of PDF4QT (MIT). | Highest leverage if the goal is "a free open Acrobat exists". No duplicated years of work. | You don't control direction. PdfCraft is three weeks old; its governance is unknown. Different stack (Rust or C++/Qt). |
| **B1. Fresh, "trustworthy core"** | Build the app on this PRD, but compete on *verified correctness*: published acceptance corpus, verifiable redaction, honest signature status, offline-by-default proof, accessibility-first reader. | Matches the PRD's principles. Differentiated from feature-count clones. Achievable scope. | Smaller feature list at launch. Needs discipline to resist parity pressure. |
| **B2. Fresh, ".NET PDF toolkit first"** | Ship an Apache-2.0 .NET library and CLI (PDFium + qpdf adapters, writer, verified redaction, OCR layer), then the Avalonia app as its first consumer. | Fills the gap left by PDFsharp's move to AGPL. The CLI/SDK is a gap competitors also have. Useful to developers early. | Two products to maintain. Desktop users wait longer. |
| **C. Proceed as written** | Full PRD, 12-month roadmap. | — | **Not recommended.** Scope exceeds what comparable funded teams delivered in years (MuPDF took about 7 years to mature redaction; Stirling has about 6 developers and $2M). |

**Suggested:** B1, or B1 with B2's library boundary. The PRD's layered architecture already separates the engine and application layers cleanly, which keeps both open.

---

## 6. Recommended revised v1 scope

Ranked by evidence-based difficulty (easiest first). Items marked **v1** are realistic for a first stable release; the rest move out.

| # | Capability | Building blocks | v1? |
|---|---|---|---|
| 1 | Reader: render, navigate, zoom, search, select, outline, thumbnails, properties | PDFium (out-of-process) | **v1** |
| 2 | Annotations: the 13 types PDFium can create, plus save and reopen | PDFium | **v1** |
| 3 | Page operations: merge, split, extract, reorder, rotate, insert, delete; images → PDF; PDF → images | PDFium + qpdf | **v1** |
| 4 | AcroForm fill, save and flatten | PDFium `FORM_*` | **v1** |
| 5 | Office → PDF via the **user-installed** LibreOffice; detect it, link to the download, don't bundle | LibreOffice (MPL-2.0, ~355 MB) | **v1** |
| 6 | Encryption and passwords | qpdf | **v1** |
| 7 | OCR invisible text layer | Tesseract 5.5.x (Apache-2.0); language data on demand | **v1** |
| 8 | **Redaction by rasterising affected pages**, full rewrite, metadata/attachment/JS scrub, then **automatic re-extraction check** | PDFium + qpdf + in-house verifier | **v1** |
| 9 | Signature **integrity-only** inspection: show trust and revocation as "not evaluated" | PDFium read API + `System.Security.Cryptography.Pkcs` | **v1** |
| 10 | Optional "Validate with veraPDF" when Java is present, plus basic tag checks (tagged? language? title?) | veraPDF (MPL-2.0 option), subprocess | **v1 (optional)** |
| 11 | CLI for page operations, OCR, encryption and redaction | Same stack | **v1** |
| — | Full signature validation (chain, OCSP/CRL, timestamps, DocMDP analysis) | .NET + BouncyCastle, or EU DSS sidecar | v2 |
| — | PAdES B-B/B-T signing | In-house writer + CMS | v2 |
| — | Single-line text replacement when the glyphs are available (no reflow) | PDFium text objects | v2 |
| — | Form-field creation; PDF/A conversion for simple cases | In-house writer | v2 |
| — | Redaction that keeps vector content; paragraph reflow editing; LTV signing; tag remediation; PDF/X; XFA (detect and warn only) | — | Later / never |

**Legal and reputational hazards:**
- **Redaction:** real leaks have caused legal sanctions and news coverage (TSA 2009, Manafort 2019, the Epstein files 2025). A 2023 study (Bland et al., PoPETs) recovered redacted text from glyph-position side channels in most tools it tested. **Never fall back silently to drawing boxes.**
- **Signatures:** 21 of 22 desktop viewers were vulnerable to signature-forgery attacks (RUB 2019), and 16 of 29 to shadow attacks (NDSS 2021). A wrong "valid" verdict can enable fraud.

---

## 7. Distribution, cost and sustainability

### 7.1 Cash cost (per year)

| Item | Cost | Notes | Conf. |
|---|---|---|---|
| Apple Developer Program | **$99** — effectively mandatory | Since Sequoia, unsigned apps need a trip to System Settings. Homebrew disables casks that fail Gatekeeper (from Sept 2026). No fee waiver for individuals. | H |
| Windows signing | **$0–$300** | Free: Microsoft Store (MSIX, re-signed by Microsoft; free for individuals since Sept 2025). Free: SignPath Foundation for OSS, once the project has a release and reputation. Azure Artifact Signing $9.99/mo, **individuals in US/Canada only**. OV certificate ~$150–300, max validity now 460 days. | H/M |
| Linux | $0 | Flathub, Snap, AppImage | H |
| CI | $0 | GitHub Actions is free for public repos on standard Windows, macOS and Linux runners | H |
| **Total** | **~$99–$400/yr** + domain | The real cost is maintainer time | — |

### 7.2 AI-development policy constraints (new, directly relevant)

- **Flathub:**
  - Requires disclosure of AI-generated code.
  - **Manifests must contain no AI-generated or AI-assisted content.**
  - AI agents may not open or automate submission PRs.
  - Reviewers may reject a submission based on how much code is generated. (H ✔)
- **Flathub also requires building from source with no prebuilt binaries.** The NuGet PDFium binaries would not be accepted, so a Flatpak means building PDFium (a Chromium-toolchain build) from source. **Ship .deb/AppImage/Snap first; treat Flathub as later work.** (H ✔)
- **NLnet GenAI policy v2.0 (2026-10-20)** bans using generative AI to produce code, design, documentation or proposals in funded work. **NLnet funding is effectively closed to a project built mainly with AI agents.** (H ✔)

### 7.3 Funding routes

- **Realistic:**
  - GitHub Sponsors (0% fee) or Open Collective (10% host fee) to cover running costs.
  - FLOSS/fund ($10k–100k, open worldwide; 2026 round status unclear).
- **Restricted:**
  - Prototype Fund requires German residence.
  - Sovereign Tech Fund does not fund user-facing apps.

### 7.4 Maintenance reality

Comparable apps survive on very few people: SumatraPDF and PDF4QT have essentially **one** maintainer each, Okular has about 2–3 core humans, and Stirling about 6 funded developers. Each represents years of near-daily work. Add the PDFium update treadmill from §3.3.

---

## 8. Proposed 2-week go/no-go spike

This replaces playbook §10 steps 3–4 and roadmap Milestone 0's engine benchmark with concrete pass/fail criteria.

| # | Prove | Pass criterion |
|---|---|---|
| 1 | **Out-of-process PDFium tile renderer** in Avalonia 12 / .NET 10: 256/512 px tiles streamed to a Skia surface | 2,000-page PDF scrolls smoothly at 25–800% zoom within a set memory ceiling, on Windows, macOS and Linux (X11 and XWayland), with GPU and software rendering |
| 2 | **Document-text accessibility** | A custom automation peer lets NVDA/Narrator, VoiceOver and Orca read a page and move line by line. If not, decide: contribute upstream, accept a limitation, or switch to Qt |
| 3 | **Printing** | A 50-page PDF prints through the native dialog on each OS; record the effort |
| 4 | **qpdf via P/Invoke** | AES-256 encrypt and decrypt round-trips; outline and metadata writes |
| 5 | **Redaction prototype** | Rasterise-and-rebuild flow; automated re-extraction finds zero residual text, images or metadata on a fixture set. Also check whether PDFium text objects can be rebuilt with the original font (the basis for v2 glyph-level redaction) |
| 6 | **Packaging in CI** | Three-runner build produces a signed Windows MSIX, a notarised macOS DMG and a Linux AppImage/.deb. Record size and startup time |

A failure on #1 or #2 is a framework decision point. A failure on #5 means redaction moves to v2.

---

## 9. Decisions needed from you

> **Decided 2026-10-21:** strategy **B1, trustworthy core**. This answers decisions 1 and 3.
> - The PRD (v0.2), roadmap, playbook, checklists, and UI copy were rescoped to the revised v1.
> - The spike is planned in `docs/06-SPIKE-PLAN.md`.
> - Decision 4 has a provisional default in PRD §4.9 (a v1 goal, gated by spike item S2).
> - Decisions 2, 5, and 6 remain open.

1. **Goal:** Is the aim (a) for a trustworthy open PDF app to *exist* — which favours contributing (option A) — or (b) to build and own this one (B1/B2)?
2. **Team:** Solo plus AI agents, or more people? This sets which v1 scope in §6 is realistic and how the 12-month roadmap should be rewritten.
3. **Differentiator:** Trustworthy core (B1), .NET toolkit first (B2), or a narrower wedge such as accessibility-first?
4. **Accessibility bar for v1:** Must screen readers read document text on all three OSes at v1? If yes, budget the upstream Avalonia work or re-evaluate Qt.
5. **AI-authorship disclosure policy:** Decide it now, given Flathub's and NLnet's rules. Flathub manifests must be human-written.
6. **New name:** Pick candidates and run trademark, domain and package-ID checks before any branding work.

## 10. Proposed changes to the PRD and roadmap (pending your decisions)

- Rename the product (UI copy doc §1 already anticipates this).
- §7.1: engine strategy becomes **PDFium + qpdf + in-house writer**; record the PDFsharp ≥ 6.5, MuPDF, iText, Poppler and Ghostscript exclusions in the licence ADR.
- §8: out-of-process PDFium becomes **mandatory**, not "where feasible".
- §4.5, §4.7, §4.8: replace with the v1/v2 split from §6.
- §4.9: state that screen-reader access to document text depends on the outcome of spike item 2.
- §6/§7: target **.NET 10 LTS** and **Avalonia 12.x MIT core only**; printing is per-OS native work.
- Roadmap: Milestones 5–6 (editing, signatures, PDF/UA) move beyond the initial stable release; add a PDFium patch cadence to Milestone 7.
- Playbook: add a rule that **packaging manifests for Flathub are human-authored**, and a dependency licence re-check on every version bump.

## 11. Local development environment (this machine)

| Item | Status |
|---|---|
| .NET SDK | **Not installed.** Install the .NET 10 SDK. |
| Git / Node / Python 3.14 | Installed |
| Java, Tesseract, LibreOffice | Not installed. Only needed for optional integrations. |
| Hardware | i5-1235U, ~8 GB RAM, Windows 11 x64. Adequate for .NET and Avalonia work; tight for running several CI-like builds and a VM at once. |
| macOS | **No Mac available.** macOS builds, notarisation and VoiceOver testing must run on CI macOS runners, or on borrowed or rented hardware for manual accessibility checks. |

## 12. Unverified items and research gaps

- PdfCraft funding, governance and actual hardening; its Windows/macOS release files were not inspected.
- Whether PDFium text objects can be recreated with an existing (subset) font — critical for glyph-level redaction (spike item 5).
- Avalonia large-document rendering performance (no published benchmarks; spike item 1).
- Exact PDFium CVE totals (keyword-based NVD counts).
- Whether Azure Artifact Signing's organisation eligibility requires 3+ years of incorporation (forum source only).
- FLOSS/fund 2026 round status; Prototype Fund details (official site blocked automated fetch).
- No official USPTO/EUIPO trademark search was run for any name.

---

## Sources (accessed 2026-10-21 unless dated)

**Competitors**
- https://github.com/storytold/pdfcraft ✔
- https://github.com/OpenAEC-Foundation/open-pdf-studio ✔
- https://github.com/Stirling-Tools/Stirling-PDF · https://docs.stirlingpdf.com/Desktop/Where-Tools-Run/ ✔ · https://www.stirling.com/pricing · https://www.opencoreventures.com/blog/open-source-creator-anthony-stirling-launches-stirling-pdf-to-build-the-first-open-core-pdf-editing-suite (2024-09-16)
- https://github.com/JakubMelka/PDF4QT ✔
- https://okular.kde.org/download/ · https://github.com/sumatrapdfreader/sumatrapdf · https://github.com/xournalpp/xournalpp · https://github.com/pdfarranger/pdfarranger · https://github.com/alam00000/bentopdf · https://github.com/torakiki/pdfsam
- https://github.com/mozilla/pdf.js · Firefox 137/145/150 release notes

**Engines and licensing**
- https://pdfium.googlesource.com/pdfium/+/refs/heads/main/public/ (`fpdf_save.h`, `fpdf_signature.h`, `fpdf_edit.h`, `fpdf_annot.h`, `fpdf_formfill.h`, `fpdfview.h`); `/LICENSE`; `/core/fpdfapi/edit/cpdf_creator.cpp`
- https://github.com/bblanchon/pdfium-binaries
- https://github.com/empira/PDFsharp/blob/master/LICENSE ✔ (AGPL-3.0 + commercial)
- https://github.com/qpdf/qpdf (README, TODO.md, v12.4.2)
- https://github.com/ArtifexSoftware/mupdf (COPYING) · https://gitlab.freedesktop.org/poppler/poppler · https://github.com/itext/itext-dotnet (LICENSE.md)
- NuGet: bblanchon.PDFium.*, PDFiumCore, PDFtoImage, PdfPig, PDFsharp
- https://services.nvd.nist.gov/rest/json/cves/2.0?keywordSearch=PDFium · https://github.com/google/oss-fuzz/tree/master/projects

**UI framework**
- https://www.nuget.org/packages/Avalonia · https://avaloniaui.net/pricing · https://avaloniaui.net/blog/building-a-sustainable-future-for-avalonia (2025-10-10)
- https://github.com/AvaloniaUI/Avalonia/issues/22168 ✔ · /pull/22178 · /pull/20735 · /discussions/22480 · /issues/14817
- https://docs.avaloniaui.net/docs/app-development/accessibility · /deployment/native-aot · /deployment/macos · /deployment/linux
- https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core
- https://www.qt.io/licensing/open-source-lgpl-obligations · https://platform.uno/docs/articles/features/accessibility/index.html

**Hard features**
- https://mupdf.com/releases/history · PDF4QT `Pdf4QtLibCore/sources/pdfredact.cpp` · https://github.com/Stirling-Tools/Stirling-PDF/pull/3936
- https://petsymposium.org/popets/2023/popets-2023-0069.php (Bland et al., redaction side channels) · https://arxiv.org/pdf/2103.02707 (Adhatarao & Lauradoux)
- https://nds.ruhr-uni-bochum.de/research/publications/vulnerability-report-attacks-bypassing-signature-v · https://www.ndss-symposium.org/ndss-paper/shadow-attacks-hiding-and-replacing-content-in-signed-pdfs/
- https://github.com/tesseract-ocr/tesseract/releases · https://ocrmypdf.readthedocs.io/en/stable/releasenotes/version17.html
- https://verapdf.org/home/ · https://pdfa.org/download-area/accessibility/MatterhornProtocol_1-02.pdf
- https://docs.pdfsharp.net/PDFsharp/Topics/PDF-Features/Signatures.html · https://www.nuget.org/packages/BouncyCastle.Cryptography

**Distribution and funding**
- https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options (updated 2026-08-29) · https://azure.microsoft.com/en-us/pricing/details/artifact-signing/ · https://signpath.org/terms
- https://blogs.windows.com/windowsdeveloper/2025/09/10/free-developer-registration-for-individual-developers-on-microsoft-store/
- https://developer.apple.com/programs/whats-included/ · https://developer.apple.com/help/account/membership/fee-waivers · https://brew.sh/2025/11/12/homebrew-5.0.0/
- https://docs.flathub.org/docs/for-app-authors/requirements ✔
- https://nlnet.nl/foundation/policies/generativeAI/2.0/ ✔ (v2.0, 2026-10-20)
- https://docs.github.com/en/billing/concepts/product-billing/github-actions · https://floss.fund · https://sovereign.tech/programs/fund
- https://cabforum.org/2025/11/17/ballot-csc-31-maximum-validity-reduction

**Name**
- https://alphapdf.com ✔ (AlphaPDF Pro, $99/yr, © 2026) · https://apps.apple.com/us/app/alpdf-pdf-converter-editor/id6738806904
