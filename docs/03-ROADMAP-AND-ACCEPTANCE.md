# Open-Source PDF Suite — Roadmap and Milestones

**Planning horizon:** About 12 months to the v1 stable release. This depends on team capacity (not yet decided), the spike results, and scope. It is a planning estimate, not a delivery promise.

**Scope basis:** the [v1]-tagged items in PRD §4 (rescoped 2026-10-21, strategy "trustworthy core"). [v2] and [Later] items are outside this roadmap. See `docs/research/2026-10-21-viability-assessment.md` for the evidence behind the scope.

## Guiding rule

Deliver working, tested capabilities in small increments. Do not add more features until the current milestone passes its acceptance gate. When a feature cannot meet the trustworthiness bar, defer it rather than shipping a weaker version.

## Milestone 0 — Research, governance, and spike

**Indicative duration:** Weeks 1–4

### Goals
- Complete the viability research (done: `docs/research/2026-10-21-viability-assessment.md`).
- Run the two-week technical spike (`docs/06-SPIKE-PLAN.md`).
- Establish the repository and governance files, with CI on Windows, macOS, and Linux.
- Create the dependency/licence inventory and the excluded-dependency list.
- Write and publish the AI-usage disclosure.
- Shortlist a new product name, with trademark, domain, and package-ID checks.
- Start the legal test corpus: a synthetic fixture generator, plus public-domain files with recorded provenance.

### Deliverables
- Spike report with measurements.
- ADRs for the UI framework, PDF engine strategy, process isolation, licensing, packaging, and redaction method.
- CI skeleton.
- Contribution, security, and AI-usage policies.
- Feature matrix showing [v1] / [v2] / [Later] status.

### Acceptance gate
- A go/no-go outcome is recorded for each spike item, with evidence.
- The ADRs listed above are accepted.
- Each supported platform has a documented, working build.
- All spike dependencies have recorded licence information.
- No unapproved licensing blocker remains hidden.

## Milestone 1 — Cross-platform reader prototype

**Indicative duration:** Weeks 4–8

### Goals
- Make the out-of-process PDFium worker the only component that parses documents.
- Open local PDF files.
- Render pages with tiles.
- Navigate, zoom, and scroll.
- Search and select text where supported.
- Display document properties and page count.
- Handle password-protected and malformed documents gracefully.
- Recover from worker crashes, and support cancellation and resource limits.
- Build an automated harness that checks network activity during offline workflows.

### Acceptance gate
- Clean build on each target platform.
- Automated tests cover core reader workflows.
- Test documents can be opened, closed, and reopened without corruption.
- The UI stays responsive during tested operations, and rendering meets the targets set by spike item S1.
- No outbound network request is made during reading (verified by test).
- No embedded file, script, or external link is executed automatically.
- Killing the worker process does not crash the application or lose unsaved state.

## Milestone 2 — Reading, annotation, and printing

**Indicative duration:** Months 2–3

### Goals
- Tabs and recent files.
- Bookmarks and document outline.
- Thumbnails and navigation panels.
- The annotation types PDFium can create:
  - highlight, underline, strikethrough, and squiggly
  - notes and text boxes
  - freehand drawing, rectangles, and ellipses
  - stamps
- Save and reopen annotations.
- Printing through the native dialog on each OS, or through the fallback chosen in the UI-framework ADR.
- A save-copy workflow that writes a temporary file and then replaces the target.
- Keyboard shortcuts and accessible UI controls.
- Screen-reader access to document text, as far as spike item S2 established.

### Acceptance gate
- Annotations survive save/reopen cycles in the supported test corpus.
- Annotations display correctly in at least one independent viewer.
- Search results navigate to the correct page and location where supported.
- Printing is verified on each supported platform.
- Keyboard navigation works for all core reader controls.
- Screen-reader testing with NVDA, VoiceOver, and Orca is done, and the results are published.
- Destructive actions are recoverable or require appropriate confirmation.
- Regression tests cover rendering and text extraction after annotation changes.

## Milestone 3 — Page operations, creation, and metadata

**Indicative duration:** Months 3–4

### Goals
- Merge, split, extract, reorder, rotate, duplicate, and delete pages.
- Insert blank pages and import pages from other PDFs.
- Create PDFs from supported images.
- Export selected pages to images, and export text where extraction is possible.
- Add images to pages.
- Edit document metadata through the structural engine (qpdf).
- Add batch processing for validated operations.

### Acceptance gate
- Page order, dimensions, orientation, and supported metadata are validated.
- Metadata edits survive save/reopen and are visible in an independent viewer.
- Operations do not unexpectedly overwrite source files.
- Partial failures are recoverable and reported.
- Batch jobs support progress, cancellation, and useful error reports.
- Output files pass structural and rendering checks.

## Milestone 4 — Forms, encryption, OCR, and conversion

**Indicative duration:** Months 4–6

### Goals
- Fill, save, reopen, and flatten AcroForms.
- Detect scripted forms and XFA forms, and explain their limitations.
- Encryption and password changes through qpdf, including AES-256.
- Detect scanned pages.
- Tesseract OCR with an invisible text layer.
- Language data downloaded only with the user's consent.
- Office-to-PDF conversion through a user-installed LibreOffice: detect it, use an isolated profile per job, and link to the download page when it is missing.

### Acceptance gate
- Form values survive save/reopen tests and display correctly in an independent viewer.
- Encrypted output opens with the correct password in independent viewers, and refuses a wrong password.
- OCR accuracy is measured against a labelled test set, and the results are published.
- Language packs and all native dependencies have recorded licences.
- OCR and conversion can be cancelled without corrupting the original.
- No network access occurs apart from consented language-data downloads (verified by test).
- Unsupported conversion cases are communicated honestly.

## Milestone 5 — Redaction, sanitisation, signature inspection, and CLI

**Indicative duration:** Months 6–8

### Goals
- Redaction using the v1 method in PRD §4.7 and its ADR: rasterise affected pages, rewrite the whole file, scrub, and verify automatically.
- A metadata sanitisation workflow.
- Signature integrity inspection:
  - "Not evaluated" for trust, revocation, and timestamps.
  - Detection of revisions added after signing.
  - Warnings before an edit invalidates a signature.
- A command-line interface for page operations, OCR, encryption, and redaction, with documented arguments and exit codes.

### Acceptance gate
- **Redaction:** canary-based tests find no residual redacted content in any of these places:
  - text and images
  - metadata and attachments
  - scripts and bookmarks
  - hidden layers and earlier revisions
- **Redaction:** the verifier catches deliberately broken redactions (negative controls).
- **Signatures:** inspection gives correct integrity results across the corpus, including documents modified after signing.
- **Signatures:** samples of the published attack classes (incremental saving, signature wrapping, shadow attacks) are never reported as unmodified signed documents.
- **Signatures:** no green "valid" indicator appears anywhere.
- CLI output and exit codes are deterministic and documented.
- An independent security review of the redaction and signature code is scheduled. It completes in Milestone 6.

## Milestone 6 — Hardening and release candidate

**Indicative duration:** Months 8–11

### Goals
- Packaging:
  - Windows: MSIX, plus a signed installer once a signing route is approved.
  - macOS: a notarised DMG.
  - Linux: AppImage and .deb.
- Test installation, update, uninstall, and recovery.
- Fuzz the worker's input paths and the project's own parsers, such as the qpdf adapter and signature parsing.
- Independent security review, including redaction and signature inspection.
- Publish the corpus results and the feature matrix.
- Accessibility review with assistive technology on each OS.
- Run the PDFium update process: check weekly, update at least monthly, and update out of band for High or Critical CVEs.
- Dependency audit, SBOM, and third-party notices.
- Recovery, backup, and crash testing.
- Complete documentation and localisation foundations.

### Acceptance gate
- Clean build and installation on every supported platform.
- No unresolved critical or high-severity security issue.
- No known data-loss defect in the release-blocking test suite.
- The bundled PDFium is no more than one month behind the latest upstream build.
- All release dependencies and notices have been reviewed.
- Installation, update, uninstall, and recovery are tested.
- Known limitations are published.
- The feature matrix reflects the actual implementation.

## Milestone 7 — Stable v1

**Indicative timing:** Around month 12

Release when the initial release definition below is met. The release is not tied to a date.

## After v1 — v2 candidates (not scheduled)

- Certificate-chain, revocation, and timestamp validation, with analysis of changes made after signing.
- PAdES B-B and B-T signing.
- Replacing text within a single text object, without reflow.
- Creating and editing form fields.
- Replacing and deleting images.
- Line, arrow, polygon, and callout annotations.
- PDF/A conversion for simple cases.
- OCR review and correction.
- Flathub distribution.

**Later or never:**
- Redaction that keeps vector content.
- Paragraph editing with reflow.
- Long-term signature validation.
- Tag remediation.
- PDF/X.
- XFA.
- A plugin API.
- Optional AI features.

## Ongoing in every milestone

- Track PDFium security fixes weekly.
- Re-check a dependency's licence on every version bump.
- Add a regression document to the corpus for every fixed document-handling defect.

## Prioritization framework

Use four priorities:

- **P0 — Release blocker:** data loss, serious security vulnerability, licensing blocker, broken build, core workflow failure, or any redaction or signature-status defect that could mislead a user.
- **P1 — Core product:** essential reader, annotation, page operations, printing, save, navigation, and accessibility of core controls.
- **P2 — v1 productivity:** form filling, encryption, OCR, office conversion, redaction, signature inspection, and CLI.
- **P3 — Beyond v1:** [v2] and [Later] items. Do not work on these before v1 unless an ADR reprioritises them.

Do not let attractive P3 features delay P0 security or P1 reliability.

## Initial release definition

The initial stable release is a trustworthy PDF reader and productivity foundation. It does not claim Acrobat parity. Its scope is the [v1] items in PRD §4.

Only advertise features that:
- Exist in the release build.
- Have documented supported behaviour.
- Pass their acceptance tests, with the corpus results published.
- Work on the platforms listed as supported.
- Have acceptable licensing and security status.

A smaller reliable release is preferable to a large collection of unreliable features.
