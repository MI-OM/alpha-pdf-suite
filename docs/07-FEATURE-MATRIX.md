# Feature Matrix

**As of:** 2026-11-01, the end of the technical spike.
**Purpose:** the honest feature matrix the product promises (PRD §1). It lists every capability in PRD §4, its target release, and what exists today, including what does not work. The published matrix must match the actual build (playbook §8), so update this file in the same pull request as any change of status.

## How to read it
- **Release** is the PRD's tag:
  - **v1:** the initial stable release
  - **v2:** the next major release
  - **Later:** no committed release
  - **Excluded:** not planned (PRD §5)
- **Status** uses the engineering checklists' states (§1), plus one for spike code:
  - **Not started**
  - **Prototyped:** spike code exists and was tested in the spike, but it is not product code and may be rewritten
  - **Implemented:** product code exists
  - **Tested:** its acceptance tests pass
  - **Supported:** documented use cases pass the compatibility and quality gates on that platform

**Nothing is Implemented, Tested or Supported yet.** Prototypes have no user interface unless the notes say so.

## Platforms

| Area | Windows | macOS | Linux |
|---|---|---|---|
| Build and tests in CI | Pass (x64) | Pass (arm64) | Pass (x64) |
| Rendering targets (S1) | Measured: every target met | Not measured interactively. The packaged app renders | Not measured interactively. The packaged app renders under Xvfb |
| Document text for screen readers (S2) | Verified through UI Automation. NVDA and Narrator checks pending | VoiceOver not tested | Orca not tested |
| Printing (S3) | Prototyped: native dialog, worker renders | Not started (estimated 2–5 days) | Not started (estimated 2–3 days) |
| Packages (S6) | MSIX installs and launches. Test certificate only | DMG installs and launches. Signed ad hoc, not notarised, and Gatekeeper rejects it | .deb and AppImage install and launch |
| Worker resource limits | Memory cap: the Job Object refuses allocations over it | Memory cap: the worker is stopped when its footprint goes over (checked every 250 ms) | Memory cap: `RLIMIT_DATA`, which the worker sets on itself, refuses allocations over it |
| Worker OS sandbox | None | None | None |
| Architectures built | x64 (arm64 not built) | arm64 (x64 not built) | x64 (arm64 not built) |
| **Supported** | **No** | **No** | **No** |

A platform is declared supported only when PRD §6's conditions are met: a clean CI build, automated tests, a native smoke test, a documented package, and platform-appropriate file dialogs, printing, keyboard conventions and accessibility testing.

## 4.1 Core reader

| Capability | Release | Status | Notes |
|---|---|---|---|
| Open local PDF files | v1 | Prototyped | From the command line or the native Open dialog. Parsed only in the worker |
| Multiple documents and tabs | v1 | Not started | The spike viewer shows one document |
| Single-page, continuous, facing-page and presentation modes | v1 | Prototyped (continuous only) | — |
| Zoom | v1 | Prototyped | Ctrl + wheel, and Ctrl + plus or minus, from 10% to 1,600%. Performance measured at 25–800% (S1) |
| Fit to width, fit to page, rotation | v1 | Not started | — |
| Page navigation | v1 | Prototyped | PageDown and PageUp, Ctrl+Home and Ctrl+End, the scroll wheel |
| Search with next, previous and match counts | v1 | Not started | The engine extracts text and its positions |
| Select and copy text | v1 | Not started | — |
| Thumbnails, bookmarks, outline, attachments panel | v1 | Not started | The engine counts attachments |
| Document properties and metadata | v1 | Prototyped (engine only) | Read by PDFium and qpdf. No user interface |
| Print preview | v1 | Not started | The Windows 11 dialog says "This app doesn't support print preview" (spike finding 22) |
| Printing | v1 | Prototyped (Windows) | Text prints as outlines, at about 1 s per page (finding 21) |
| Recently opened files | v1 | Not started | — |
| Password-protected documents | v1 | Prototyped (engine only) | The worker and engine open with a password. The viewer has no password prompt and says so |
| Clear error messages | v1 | Prototyped | Messages for unreadable files and worker restarts, from the UI copy rules |

## 4.2 Annotation and review

| Capability | Release | Status | Notes |
|---|---|---|---|
| Highlight, underline, strikethrough | v1 | Not started | — |
| Sticky notes and text comments | v1 | Not started | — |
| Freehand drawing; rectangles and ellipses | v1 | Not started | — |
| Text boxes; stamps | v1 | Not started | — |
| Colour, opacity and thickness | v1 | Not started | — |
| Select, move, edit and delete annotations | v1 | Not started | — |
| Annotation visibility | v1 | Not started | — |
| Save and reopen without loss; flatten on request | v1 | Not started | — |
| Lines, arrows, polygons, polylines; callouts | v2 | Not started | PDFium cannot create these; they need the in-house writer |
| Import and export (FDF, XFDF) | v2 | Not started | PDFium does not support these formats |

## 4.3 Page organisation

| Capability | Release | Status | Notes |
|---|---|---|---|
| Insert pages from another PDF | v1 | Not started | The engine imports pages by range; the redactor and fixture generator use it |
| Delete, rotate, reorder, duplicate pages | v1 | Not started | — |
| Extract, split, merge; insert blank pages; export pages | v1 | Not started | — |
| Undo and redo; preview changes | v1 | Not started | — |
| Save to a new file by default for destructive operations | v1 | Not started | — |

## 4.4 Create and convert

| Capability | Release | Status | Notes |
|---|---|---|---|
| Create PDFs from images | v1 | Not started | — |
| Create PDFs from office documents through a user-installed LibreOffice | v1 | Not started | LibreOffice is never bundled |
| Export pages to images; export text | v1 | Not started | — |
| Convert scanned documents through OCR | v1 | Not started | See 4.6 |
| Batch processing | v1 | Not started | — |

## 4.5 Editing

| Capability | Release | Status | Notes |
|---|---|---|---|
| Edit title, author, subject, keywords | v1 | Prototyped (no user interface) | Through qpdf's object API, in the worker, with Unicode checked by PDFium (S4) |
| Add images to pages | v1 | Not started | — |
| Explain why content cannot be edited; offer Save As | v1 | Not started | — |
| Replace or delete existing images | v2 | Not started | — |
| Replace text within one text object, no reflow | v2 | Not started | — |
| Paragraph editing with reflow | Later | Not started | — |

## 4.6 OCR

| Capability | Release | Status | Notes |
|---|---|---|---|
| Detect scanned pages; OCR with an invisible text layer | v1 | Not started | Tesseract 5.x is planned |
| Several languages, downloaded only with consent | v1 | Not started | — |
| Progress, cancellation, error details; local processing | v1 | Not started | — |
| Review and correct OCR results | v2 | Not started | — |

## 4.7 Security and document protection

| Capability | Release | Status | Notes |
|---|---|---|---|
| Open password-protected PDFs | v1 | Prototyped (engine only) | See 4.1 |
| AES-256 encryption and permissions | v1 | Prototyped (no user interface) | Through qpdf, in the worker. Verified on all three OSes, with PDFium refusing wrong passwords (S4) |
| Metadata sanitisation workflow | v1 | Not started | The redaction scrub removes document-level metadata (S5) |
| Redaction (rasterise, rewrite, scrub, verify) | v1 | Prototyped (no user interface) | Every canary removed; every negative control rejected; all three OSes (S5; ADR-0006). Runs in the worker |
| Redaction that keeps vector content and text | Later | Not started | A glyph-level probe works for some fonts (finding 36) |
| Warn about embedded files, suspicious links, active content | v1 | Not started | — |
| Never execute embedded files, scripts or links automatically | v1 | Prototyped (by design) | The PDFium build has no JavaScript engine. The spike viewer does not follow links or open attachments at all |
| Confirm before opening external links | v1 | Not started | — |
| Warn when an edit will invalidate a signature | v1 | Not started | — |

## 4.8 Digital signatures

| Capability | Release | Status | Notes |
|---|---|---|---|
| List signatures; check byte-range integrity | v1 | Not started | — |
| Report revisions added after signing | v1 | Not started | — |
| Show signer certificate details | v1 | Not started | — |
| Show trust, revocation and timestamp as "Not evaluated" | v1 | Not started | — |
| Validate chain, revocation, timestamps; DocMDP and FieldMDP analysis | v2 | Not started | — |
| PAdES B-B and B-T signing | v2 | Not started | — |
| Long-term validation | Later | Not started | — |

## 4.9 Accessibility and standards

| Capability | Release | Status | Notes |
|---|---|---|---|
| Keyboard navigation and accessible controls | v1 | Prototyped (reader keys) | Reading keys and standard buttons only |
| Screen-reader access to document text | v1 goal | Prototyped (Windows) | A focusable element per line. Verified with UI Automation; real screen readers pending (S2). No "say all", character navigation or selection (finding 17). Reading order follows the file, not the layout (finding 16). Hidden-layer text is read too (finding 32) |
| Basic tagged-PDF checks | v1 | Not started | — |
| PDF/A and PDF/UA validation with veraPDF (optional) | v1 | Not started | Machine checks only; needs Java |
| Font diagnostics, reading-order inspection, simple PDF/A conversion | v2 | Not started | — |
| Tag remediation | Later | Not started | — |
| PDF/X preflight | Excluded | — | No open-source validator exists |

## 4.10 Automation

| Capability | Release | Status | Notes |
|---|---|---|---|
| Command-line interface: page operations, OCR, encryption, redaction | v1 | Not started | The worker's `--self-test` is a CI check, not a user command |
| Batch operations, structured logs, exit codes, safe overwrite | v1 | Not started | — |
| API or plugin boundary | Later | Not started | — |

## 4.11 Optional AI features

| Capability | Release | Status | Notes |
|---|---|---|---|
| Local question-answering, summaries, semantic search | Later | Not started | Optional and off by default if ever built (PRD §4.11) |

## 4.12 Forms

| Capability | Release | Status | Notes |
|---|---|---|---|
| Fill text fields, checkboxes, radio buttons and lists; save; flatten | v1 | Not started | — |
| Detect scripted forms and explain that scripts do not run | v1 | Not started | The PDFium build has no JavaScript engine |
| Create and edit form fields | v2 | Not started | — |
| XFA forms | Later | Not started | To be detected and explained, not supported |

## Not supported in v1
From PRD §5. The product will say so where a user meets these cases:
- editing existing text, and paragraph reflow
- redaction that keeps vector content and text on affected pages
- digital signing, and validation of certificate chains, revocation, timestamps or long-term signatures
- form-field authoring, form JavaScript, and XFA forms
- PDF/A conversion, tag remediation, and PDF/X preflight
- office conversion without a user-installed LibreOffice
- Flathub distribution
- legal certification of signatures, and any claim of PDF/A, PDF/UA, PDF/X or regulatory compliance without evidence
