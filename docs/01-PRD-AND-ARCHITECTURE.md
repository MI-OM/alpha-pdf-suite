# Open-Source PDF Suite
## Product Requirements Document and Technical Architecture

**Document status:** Draft v0.2 (rescoped 2026-10-21; see `docs/research/2026-10-21-viability-assessment.md`)  
**Product type:** Free, open-source, cross-platform desktop PDF application  
**Target platforms:** Windows, macOS, Linux  
**Proposed application licence:** Apache License 2.0, subject to final review  
**Architecture status:** Proposed; decisions must be validated by technical spikes (see `docs/06-SPIKE-PLAN.md`)  
**Primary principle:** Build a trustworthy, complete PDF productivity application without sacrificing user freedom, security, or document fidelity.
**Product working name:** Alpha PDF Suite (temporary; an unrelated commercial PDF product named "AlphaPDF" already exists, so the product must be renamed before any public release)

---

## 1. Product vision

Build a professional-grade PDF application that lets individuals and organizations read, create, organize, annotate, edit, convert, protect, inspect, and process PDF documents without requiring a subscription or uploading private files to a cloud service.

The product should become a credible open-source alternative to established PDF productivity software. It competes first on verified trustworthiness—reliability, document fidelity, privacy, accessibility, and security—and only then on breadth of functionality.

The application must be designed for Windows, macOS, and Linux from the beginning. Platform-specific capabilities may use separate adapters, but the document model and core business logic must remain portable.

### Positioning: the trustworthy core

Decided 2026-10-21 (strategy B1 in the viability assessment).

Several open-source projects launched in 2026 aim at Acrobat-style breadth, for example PdfCraft, Open PDF Studio, and Stirling-PDF's desktop app. This product does not race them on feature count. It differentiates on evidence that users can trust it:

- A published, legally distributable acceptance corpus, and the results of every release against it.
- Redaction whose output is automatically re-checked for residual content, and which never silently falls back to drawing boxes.
- Signature status that separates integrity, trust, revocation, and timestamps, and says "Not evaluated" rather than implying trust.
- Offline-by-default behaviour verified by automated network tests.
- An accessible reader, tested with real assistive technology on each supported OS.
- An honest feature matrix that lists every unsupported case.

When a feature cannot meet this bar in the current release, it is deferred rather than shipped in a weaker form.

### Product principles

1. Free to use, modify, and redistribute under the chosen project licence.
2. No paid feature tiers or subscription mechanisms.
3. Local-first operation, with no account required for core features.
4. No document uploads, telemetry, or external network communication without a clear user-facing reason and explicit consent where appropriate.
5. Security and privacy are architectural requirements, not final-stage additions.
6. Preserve document content and structure whenever possible.
7. Never claim a feature works until it passes its acceptance tests.
8. Maintain an honest feature-support matrix and clearly communicate unsupported cases.
9. Prefer standards-based formats and interoperable outputs.
10. Keep the architecture modular enough to replace a PDF engine or other dependency.

## 2. Problem statement

PDF users frequently need several applications or paid services to complete common workflows: reading, commenting, filling forms, rearranging pages, converting documents, OCR, redaction, signatures, accessibility remediation, and batch processing.

An open-source alternative can reduce vendor lock-in, improve transparency, enable community inspection, and give users control over local documents. However, PDF is a complex format. A credible product must handle malformed files, unusual fonts, embedded images, encryption, forms, signatures, tagged content, and very large documents without data loss or unsafe behavior.

The product must therefore prioritize document correctness and security over the number of buttons displayed in the interface.

## 3. Target users

### Primary users

- Students, researchers, educators, and independent professionals.
- Individuals who regularly read, annotate, sign, or organize PDFs.
- Nonprofits, charities, and community organizations.
- Small businesses that need document workflows without recurring software costs.
- Privacy-conscious users who prefer local processing.
- Developers and technical users who need automation and batch processing.

### Secondary users

- Accessibility specialists.
- Records-management and archival teams.
- Legal and compliance professionals, subject to validated workflows and clear limitations.
- Organizations that need offline or managed desktop deployment.
- Open-source contributors and downstream distributors.

The product must not claim legal, regulatory, archival, or accessibility compliance merely because a feature is present. Applicable standards must be implemented and tested.

## 4. Product scope

Each capability is tagged with its target release:

- **[v1]**: the initial stable release. Only [v1] items are in scope for the current roadmap.
- **[v2]**: the following major release, prioritised after 1.0.
- **[Later]**: no committed release.

Tags reflect the 2026-10-21 viability assessment. A section heading's tag applies to every item in it unless an item carries its own tag.

### 4.1 Core reader — [v1]

- Open local PDF files.
- Multiple documents and tabs.
- Single-page, continuous, facing-page, and suitable presentation modes.
- Zoom, fit-to-width, fit-to-page, rotation, and page navigation.
- Search text with next/previous matches and result counts.
- Select and copy text where the document permits it.
- Page thumbnails, bookmarks, document outline, and attachments panel.
- Document properties and metadata.
- Print preview and printing through supported operating-system facilities. Avalonia has no printing API, so this is native work on each OS (spike item S3).
- Recently opened files, configurable and stored locally.
- Password-protected document handling.
- Clear error messages for unsupported, corrupted, or inaccessible documents.

### 4.2 Annotation and review — [v1]

- Highlight, underline, and strikethrough.
- Sticky notes and text comments.
- Freehand drawing and basic shapes (rectangles and ellipses).
- [v2] Lines, arrows, polygons, and polylines. PDFium cannot create these annotation types, so they need the in-house writer (§7.1).
- Text boxes.
- [v2] Callouts.
- Stamps.
- Annotation colour, opacity, and thickness controls.
- Annotation selection, movement, editing, and deletion.
- Annotation visibility controls.
- Save and reopen annotations without loss.
- Flatten annotations when explicitly requested.
- [v2] Import and export annotations (FDF/XFDF). PDFium does not support these formats.
- Verify that saving does not unexpectedly alter unrelated document content.

### 4.3 Page organization — [v1]

- Insert pages from another PDF.
- Delete, rotate, reorder, and duplicate pages.
- Extract selected pages to a new PDF.
- Split a document by page range or defined rules.
- Merge multiple PDFs.
- Insert blank pages.
- Export selected pages.
- Undo and redo for supported editing operations.
- Preview page changes before committing them.
- Save to a new file by default when destructive operations could damage the source.

### 4.4 Create and convert — [v1]

- Create PDFs from supported images.
- Create PDFs from supported office documents using a user-installed LibreOffice, detected at runtime and run headless with an isolated profile for each job. LibreOffice is not bundled (its installer is about 355 MB). If it is missing, explain why and link to its download page.
- Export PDF pages to images.
- Export text where extraction is possible.
- Convert scanned documents through OCR.
- Batch process supported files.
- Preserve page order, orientation, and dimensions.
- Explain conversion limitations rather than silently losing formatting.

Office-document conversion must use a separately evaluated, licence-compatible provider, such as an optional LibreOffice integration. Do not assume reliable Word/Excel conversion can be implemented by the PDF rendering engine alone.

### 4.5 Editing — mostly [v2]

- [v1] Edit document metadata (title, author, subject, keywords) through the structural engine.
- [v1] Add images to pages.
- [v2] Replace or delete existing images.
- [v2] Replace text within a single existing text object when the embedded font contains every required glyph. No reflow.
- [Later] Paragraph editing with reflow.
- [v1] Explain when content is embedded in a scan, outlined, encrypted, signed, or otherwise unsuitable for direct editing.
- [v1] Preserve the original file and offer Save As when editing could cause irreversible changes.

Text editing is a major technical risk:

- Embedded fonts are usually subsets, so glyphs are missing.
- Text is stored as positioned glyph runs, with nothing to reflow.
- Edits make tags stale and break signatures.

No permissively licensed library solves these problems. Do not promise Word-like editing of every PDF. Implement the [v2] subset, and expand it only based on document fidelity benchmarks.

### 4.6 OCR — [v1]

- Detect likely image-only or scanned pages.
- Run OCR on selected pages or entire documents.
- Support multiple OCR languages. Language data requires network access (§9), so download it only on demand and only after the user agrees.
- [v2] Let users review and correct OCR results.
- Preserve the visual page appearance when adding a searchable text layer.
- Show OCR progress, cancellation, language selection, and failure details.
- Process locally by default.

Tesseract 5.x is the selected OCR engine for v1: https://github.com/tesseract-ocr/tesseract

- Its upstream code is Apache 2.0, and so are the tessdata_best language models. Its native dependencies must still be inventoried.
- Existing .NET wrappers are stale or Windows-only. Plan to ship libtesseract for each platform, or call the `tesseract` command-line tool.
- The searchable text layer is written as invisible text over the original page image.

### 4.7 Security and document protection — [v1]

- Open password-protected PDFs supported by the selected engine.
- Apply PDF encryption (including AES-256) and permissions settings through the structural engine (qpdf). PDFium cannot write encryption.
- Remove metadata through an explicit sanitization workflow.
- Provide a redaction workflow that removes underlying content, not merely covers it visually. The v1 method:
  1. The user marks areas and reviews them.
  2. Every page that contains a redaction area is rasterised and replaced by its image, with the areas filled. Optional OCR then restores searchable text outside the redacted areas.
  3. The output is written as a new file with a full (non-incremental) rewrite, which removes earlier revisions and unreferenced objects.
  4. Metadata, attachments, JavaScript, hidden optional content, and structure-tree text (ActualText, alt text) that could repeat removed content are removed or scrubbed.
  5. An automatic check re-extracts text, images, and metadata from the output and must find no redacted content. If the check fails, the file is not saved.

  Before the redactions are applied, the UI must state that affected pages lose selectable text and vector quality.
- [Later] Redaction that keeps vector content and text on affected pages. This needs glyph-level text splitting, image pixel scrubbing, and handling of form XObjects shared between pages. No permissively licensed library provides these.
- Detect and warn about embedded files, suspicious links, and unsupported active content where technically possible.
- Never execute embedded files automatically.
- Never automatically navigate to an external URL from a PDF.
- Require confirmation before opening external links.
- Distinguish password protection, cryptographic encryption, document permissions, digital signatures, and certificate trust.
- Preserve signature evidence when required and warn when an edit will invalidate a signature.

### 4.8 Digital signatures — inspection [v1], validation and signing [v2]

- [v1] List existing signature fields and check each signature's cryptographic integrity over its byte range.
- [v1] Report whether the signed revision covers the whole file, or how many revisions were added after signing.
- [v1] Show signer certificate details.
- [v1] Show certificate-chain trust, revocation, and timestamp status as **Not evaluated**, never as passed.
- [v2] Validate the certificate chain, revocation (OCSP/CRL), and timestamps. Analyse changes made after signing against DocMDP/FieldMDP permissions.
- [v2] PAdES B-B and B-T signing, only after cryptographic and interoperability tests. Make certificate and private-key handling explicit.
- [Later] Long-term validation (LTV).
- Never label a signature trusted merely because a signature field exists or a cryptographic check succeeds.
- Do not silently invalidate signatures when modifying a signed document.

PDFium only exposes signature data; it neither validates nor signs. Published research found most desktop viewers vulnerable to signature attacks: 21 of 22 to the attacks reported by Ruhr University Bochum (2019), and 16 of 29 to shadow attacks (NDSS 2021). Signature code therefore needs independent security review, and the published attack classes become regression tests.

### 4.9 Accessibility and standards

- [v1] Keyboard navigation and accessible UI controls on every supported OS.
- [v1 goal] Screen-reader access to document text on every supported OS: read a page and move through it line by line.
  - Avalonia currently exposes no text pattern to assistive technology, so this depends on spike item S2.
  - If the spike does not achieve it, v1 ships with a published known limitation, and the upstream work is tracked.
- [v1] Basic tagged-PDF checks: whether the document is tagged, and whether it has a language, title, and alt text.
- [v1, optional] PDF/A and PDF/UA validation with veraPDF (used under its MPL-2.0 option), run as a subprocess when Java is installed.
  - Present results as machine checks only.
  - The Matterhorn Protocol has 47 failure conditions that need human judgement.
- [v2] Font embedding and substitution diagnostics, reading-order inspection, and PDF/A conversion for simple cases.
- [Later] Tag remediation.
- Excluded: PDF/X preflight. No open-source validator exists.

These formats and standards have different requirements. Implement each against authoritative specifications and test suites. Do not advertise certification or conformance without evidence.

### 4.10 Automation — [v1], except where marked

After core stability:
- Batch conversion and page operations.
- A documented command-line interface covering page operations, OCR, encryption, and redaction.
- Deterministic, scriptable operations.
- Structured logs that exclude document contents by default.
- Machine-readable exit codes and reports.
- Safe overwrite controls.
- [Later] A documented API or plugin boundary, only when the security model is clear.

### 4.11 Optional AI features — [Later]

Potential future features include local document question-answering, summaries, and semantic search. They are not required for the first release.

Any future AI capability must:
- Be optional and disabled by default until configured.
- Identify the source pages or text supporting answers.
- Distinguish extracted document facts from model-generated interpretation.
- Never execute instructions found inside a document.
- Never upload a document without explicit consent.
- Be evaluated against prompt injection, hallucination, privacy, and citation accuracy.
- Work without making AI a dependency for ordinary PDF operations.

### 4.12 Forms — filling [v1], authoring [v2]

- [v1] Fill AcroForm text fields, checkboxes, radio buttons, and choice lists. Save, reopen, and flatten.
- [v1] The v1 engine build does not run form JavaScript, so calculated fields and scripted validation do not update. Detect scripted forms and tell the user.
- [v2] Create and edit form fields.
- [Later] XFA forms: detect them and explain that they are not supported. XFA is deprecated in PDF 2.0.

## 5. Explicit exclusions for the initial release

- Mandatory user accounts or cloud storage.
- Paid editions, subscriptions, advertisements, or in-app purchases.
- Automatic document uploads.
- Automatic execution of PDF attachments, scripts, or external links.
- Full parity with every feature in Adobe Acrobat.
- Guaranteed perfect conversion of every office document.
- Claims that every PDF can be edited as if it were a word processor.
- Legal certification of digital signatures.
- Unsupported claims of PDF/A, PDF/UA, PDF/X, or regulatory compliance.
- Mobile apps before desktop stability is established.
- Proprietary or undocumented dependencies that create avoidable lock-in.
- Editing existing text, and paragraph reflow.
- Redaction that keeps vector content on affected pages.
- Digital signing, and validation of certificate chains, revocation, timestamps, or long-term signatures.
- Form-field authoring, form JavaScript, and XFA forms.
- PDF/A conversion, tag remediation, and PDF/X preflight.
- A bundled office-conversion engine.
- Flathub distribution, until two conditions are met: PDFium can be built from source in the Flathub build, and the manifest rules in the agent playbook are satisfied.

## 6. Cross-platform requirements

The initial desktop targets are:
- Windows 10/11, with exact supported versions decided after deployment testing.
- Current supported macOS releases, with Intel/Apple Silicon support decided through build testing.
- Major Linux distributions, with packaging and support matrix documented.

Initial technical baseline, to be confirmed or revised by the spike ADRs:
- .NET 10 LTS (supported until 2028-11-14) and Avalonia 12.x.
- x64 and arm64 on each OS, matching the available PDFium builds.
- Linux: X11 is the primary tested session. Wayland runs through XWayland until Avalonia's native Wayland backend matures.
- v1 packages:
  - Windows: MSIX through the Microsoft Store, plus a signed installer once a signing route is approved.
  - macOS: a signed and notarised DMG.
  - Linux: AppImage and .deb.

All platform targets must have:
- A successful clean build in CI.
- Automated tests for shared logic.
- At least one smoke-test workflow on the native platform.
- A documented installer/package.
- Platform-appropriate file dialogs, print integration, keyboard conventions, and accessibility testing.

The team must not declare a platform supported merely because the application compiles for it.

## 7. Proposed architecture

### 7.1 Technology choices

These choices come from the 2026-10-21 viability assessment. Each is confirmed or revised by an ADR after the spike.

**UI and application framework:** Avalonia UI 12.x on .NET 10 LTS.
- Use MIT core packages only. Commercial Avalonia components and tooling, including the paid PDF viewer and the Parcel packager, must not be used.
- This choice is conditional on spike items S1–S3 and S6. Qt 6 is the fallback if rendering or document-text accessibility fails.

**PDF engine:** PDFium (BSD-3/Apache-2.0).
- Accessed through PDFiumCore or project-owned P/Invoke bindings, using the non-V8 builds from bblanchon/pdfium-binaries.
- Runs out-of-process (§8).
- Used for rendering, text extraction, search, annotations, page operations, form filling, and page-object editing.
- Keep the engine behind an adapter so it can be replaced.

**Structural engine:** qpdf (Apache-2.0) through its C API. Used for:
- encryption and password changes
- metadata, outline, and page-label writes
- linearisation
- full-rewrite cleanup after redaction

**In-house writer:** a small C# low-level object writer and incremental-update writer for features neither engine provides: [v2] signing, Redact annotations, form-field creation, and annotation types PDFium cannot create. Build it only when a [v2] feature needs it.

**OCR:** Tesseract 5.x (Apache-2.0). See §4.6.

**Office conversion:** user-installed LibreOffice (MPL-2.0), detected at runtime. Keep conversion providers isolated from the core application.

**Signature cryptography:** `System.Security.Cryptography.Pkcs` (MIT). BouncyCastle .NET is a candidate for [v2] revocation and timestamp work.

**Optional validation:** the veraPDF command-line tool (MPL-2.0 option), run as a subprocess when Java is present.

**Excluded:**
- For licence reasons:
  - MuPDF, iText (including pdfSweep), and Ghostscript, which are AGPL.
  - Poppler, which is GPL.
  - PDFsharp/MigraDoc 6.5.0 and later, which have been AGPL since 2026-10-14.
- Unmaintained bindings: Docnet.Core and PdfiumViewer.

**Testing:** Use the .NET testing ecosystem for unit and integration tests, plus cross-platform UI smoke tests and corpus-based document tests.

**CI:** Use a public repository with automated builds, tests, formatting, dependency checks, and licence checks on supported operating systems.

No library or framework is approved solely because it is popular or open source. Each choice requires an Architecture Decision Record (ADR), a version-pinned dependency inventory, and a documented licence review.

### 7.2 Layered architecture

1. **Presentation layer**
   - Windows, tabs, panels, dialogs, toolbars, accessibility, themes.
   - Must not implement PDF parsing or cryptographic logic.

2. **Application layer**
   - Commands, workflows, document sessions, undo/redo, progress, cancellation, validation.
   - Coordinates features without depending on UI-specific controls.

3. **Domain layer**
   - Document session state, page references, annotations, operation results, errors, permissions, and feature capabilities.
   - Uses stable interfaces and immutable state where practical.

4. **PDF engine abstraction**
   - Open/close document.
   - Render page.
   - Extract text.
   - Search text.
   - Read metadata and page information.
   - Perform supported page operations.
   - Save/export.
   - Expose capability and limitation information.

5. **Engine adapters**
   - One adapter per selected engine or integration.
   - Engine-specific types must not leak into the UI or domain layer.

6. **Security boundary**
   - Isolate risky parsing and rendering where supported.
   - Enforce resource limits and cancellation.
   - Validate file paths and external actions.
   - Separate document processing from privileged application operations.

7. **Local persistence**
   - Preferences, recent-file metadata, optional local indexes, and recovery state.
   - Never store full document content in logs.
   - Make cache location and cleanup behaviour predictable.

8. **Optional integrations**
   - OCR, office conversion, signature providers, and future AI.
   - Integrations must be replaceable and independently testable.

### 7.3 Dependency direction

Presentation → Application → Domain.

Engine adapters and external providers implement interfaces defined by the application/domain boundary. Domain code must not depend on a specific PDF engine, UI toolkit, cloud provider, or AI vendor.

### 7.4 Proposed repository structure

    /
      README.md
      LICENSE
      NOTICE
      CONTRIBUTING.md
      CODE_OF_CONDUCT.md
      SECURITY.md
      CHANGELOG.md
      global.json
      Directory.Build.props
      Directory.Packages.props
      src/
        PdfSuite.App/
        PdfSuite.Application/
        PdfSuite.Domain/
        PdfSuite.Infrastructure/
        PdfSuite.Pdf.Abstractions/
        PdfSuite.Pdf.Pdfium/
        PdfSuite.Ocr.Abstractions/
        PdfSuite.Ocr.Tesseract/
        PdfSuite.Conversion.Abstractions/
      tests/
        PdfSuite.UnitTests/
        PdfSuite.IntegrationTests/
        PdfSuite.DocumentCorpusTests/
        PdfSuite.SecurityTests/
      benchmarks/
      docs/
        architecture/
        adr/
        research/
        product/
        security/
        licensing/
      scripts/
      .github/
        workflows/
        ISSUE_TEMPLATE/

Adjust this structure only through a documented ADR; do not add projects merely to create an appearance of complexity.

The 2026-10-21 rescope proposes two additions: `PdfSuite.Pdf.Qpdf/` (the structural adapter) and `PdfSuite.Pdf.Worker/` (the out-of-process engine host). Both are confirmed or rejected by the spike ADRs.

## 8. Security architecture

PDFs are untrusted input. A valid-looking file may exploit a parser, exhaust memory, contain malicious links, or mislead a user.

Required controls:
- Use supported, maintained engine versions. Track PDFium security fixes weekly. Ship a PDFium update at least monthly, and out of band for High or Critical CVEs.
- Keep dependencies updated and inventory them.
- Run fuzzing against parsers and transformation paths.
- Test malformed, encrypted, oversized, deeply structured, and resource-intensive PDFs.
- Apply page-count, image-size, memory, execution-time, and output-size limits where appropriate.
- Support cancellation without corrupting the original.
- Do not execute embedded JavaScript, files, launch actions, or external links automatically.
- Run PDFium, and any other parser of untrusted documents, in a separate worker process with a single-threaded request queue. This is mandatory, for three reasons:
  - PDFium's public API is not thread-safe.
  - The prebuilt binaries are built without PartitionAlloc hardening.
  - About two dozen High-severity PDFium CVEs were published in 2026.

  Apply OS-level sandboxing to the worker where available, and verify the actual protection on each operating system.
- Never describe an in-process parser as sandboxed.
- Do not run untrusted documents with unnecessary privileges.
- Validate all output files and write to temporary files before finalizing saves.
- Avoid logging document text, passwords, signing keys, or private content.
- Provide responsible vulnerability reporting and coordinated release procedures.

Redaction acceptance tests must inspect saved output for the allegedly removed text, metadata, images, alternate content, and other relevant recoverable representations. Visual concealment alone is not redaction.

## 9. Privacy and network behaviour

Core document features must work offline after installation.

- No account is required.
- No document telemetry by default.
- No analytics or crash reporting that uploads data by default.
- No cloud dependency for viewing, searching, annotating, or local file operations.
- External link opening requires a user action.
- Network access for certificate revocation, timestamping, updates, or downloads must be documented and controlled.
- Provide a clear privacy statement and a network-behaviour inventory.
- Tests must verify that common offline workflows do not make unexpected outbound requests.

## 10. Open-source licensing and governance

Apache 2.0 is the provisional licence for original project code. Before publishing a release:
- Review the chosen framework, PDF engine, native binaries, OCR libraries, codecs, fonts, language data, conversion tools, test fixtures, and transitive dependencies.
- Record version, source, licence identifier, copyright notices, distribution method, modifications, and required attributions.
- Generate and review a software bill of materials (SBOM).
- Preserve required licence texts and notices.
- Review compatibility with the project licence and each redistribution format.
- Document source-available requirements and any relinking obligations when applicable.
- Do not bundle components with unclear or incompatible terms without an explicit decision.
- Require contributors to agree to the published contribution policy.
- Do not claim that a dependency is legally approved merely because a scanner reports an SPDX identifier.
- Re-check each dependency's licence on every version bump. Licences change: PDFsharp moved from MIT to AGPL-3.0 in version 6.5.0 (2026-10-14).
- Keep the list of excluded dependencies in §7.1 current.
- Publish how AI tools are used to develop the project. Distribution channels and funders have their own rules:
  - Flathub requires disclosure of AI-generated code and human-authored manifests.
  - NLnet does not fund work produced with generative AI.

Official starting points:
- Apache License 2.0: https://www.apache.org/licenses/LICENSE-2.0
- Open Source Initiative: https://opensource.org/licenses
- Avalonia: https://github.com/AvaloniaUI/Avalonia
- PDFium: https://pdfium.googlesource.com/pdfium/
- Tesseract: https://github.com/tesseract-ocr/tesseract
- Qt licensing comparison, if Qt is evaluated: https://doc.qt.io/qt-6/licensing.html

This is engineering guidance, not legal advice.

## 11. Quality attributes and measurable goals

Initial proposed targets must be validated by benchmarking:
- Launch to usable empty window: under 3 seconds on the agreed reference machine.
- First visible page of an ordinary local PDF: under 2 seconds on the agreed reference machine.
- Smooth navigation for common documents without UI blocking.
- A 2,000-page document scrolls without blocking the UI at 25–800% zoom, within a documented memory ceiling set by spike item S1.
- Search, OCR, conversion, and rendering must support cancellation where feasible.
- No data loss in tested save/reopen cycles.
- No unresolved critical or high-severity security issue at release.
- All supported platforms build successfully in CI.
- No feature is marked complete without acceptance tests.
- Every release includes dependency/licence inventory and known limitations.

Benchmark hardware, operating system, document corpus, and measurement methodology must be documented before targets become release gates.

## 12. Definition of done

A feature is done only when:
1. Requirements and supported behaviour are documented.
2. Implementation is complete without fake or placeholder functionality.
3. Unit/integration tests cover relevant logic.
4. Regression tests cover affected document workflows.
5. Error and cancellation paths have been tested.
6. Accessibility and keyboard behaviour are reviewed where applicable.
7. Security implications are reviewed.
8. Dependencies and licences are recorded.
9. User-facing copy explains limitations accurately.
10. Documentation and screenshots reflect actual behaviour.
11. CI passes on relevant target platforms.
12. A reviewer other than the implementing agent approves the change.

## 13. Release strategy

- Spike (2 weeks): go/no-go on the framework, engine isolation, document-text accessibility, printing, the structural engine, the redaction approach, and packaging (`docs/06-SPIKE-PLAN.md`).
- Prototype: validate UI, out-of-process engine integration, and basic reading.
- Alpha: stable reader, search, annotations, page operations, printing, automated tests.
- Beta: form filling, encryption, metadata, OCR, office conversion, redaction, signature inspection, CLI, security hardening.
- Release candidate: published corpus results, accessibility review with assistive technology, installer testing, documentation, and independent security review of the redaction and signature code.
- Stable 1.0: only [v1] features meeting published acceptance criteria; unsupported features clearly identified.
- v2: the [v2] items in §4, prioritised after 1.0 based on user feedback and corpus results.

The product roadmap is capability-driven, not a promise of complete parity with any competitor by a specific date.
