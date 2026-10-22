# Alpha PDF Suite — Engineering Controls and Initial Backlog

## 1. Rules for protecting the project

### Never confuse these three licences

1. The licence for our own source code.
2. The licences for third-party dependencies.
3. The licence or terms governing a particular document, font, test file, or other asset.

The application's Apache 2.0 licence does not automatically relicense its dependencies or grant rights to every sample document included in the repository.

### Never confuse these three states

- Implemented: code exists.
- Tested: defined tests have passed.
- Supported: documented use cases have passed the required compatibility and quality gates.

A feature may be implemented without being adequately tested or supported.

### Never confuse these PDF security concepts

- Password protection.
- Encryption.
- Document permission flags.
- Digital signature integrity.
- Certificate-chain trust.
- Identity verification.
- Certificate revocation status.
- Timestamp validation.
- Redaction.
- Visual covering of content.

Each has different behaviour and limitations.

## 2. Dependency approval checklist

Before adding any runtime dependency, record:

- Package and upstream project.
- Exact version and source repository.
- Licence identifier and licence text.
- Transitive dependencies.
- Native binaries and codecs.
- Maintainer activity and security response.
- Supported operating systems and architectures.
- Known vulnerabilities.
- Distribution and attribution obligations.
- Whether source code or relinking instructions are required.
- Whether the dependency contacts external services.
- Whether it is essential or replaceable.
- Evidence supporting the decision.
- Reviewer and approval date.
- Licence re-checked at this exact version. Repeat this check on every version bump.

Do not merge a dependency with unresolved licence compatibility into a release branch.

### Excluded dependencies

Do not add these without a new licensing ADR:

- **AGPL:** MuPDF, iText (including pdfSweep), and Ghostscript.
- **GPL:** Poppler.
- **AGPL since 2026-10-14:** PDFsharp/MigraDoc 6.5.0 and later.
- **Commercial:** paid Avalonia components and tooling.

Avoid unmaintained bindings such as Docnet.Core and PdfiumViewer.

## 3. Document test corpus

Build a legally distributable test corpus. Record the origin, licence, expected results, and relevant features for every file.

Include:

- Simple text PDF.
- Image-only scanned PDF.
- Searchable scan with an existing OCR layer.
- Multi-column document.
- Document with unusual fonts.
- Document with embedded fonts and images.
- Large page dimensions.
- Rotated pages.
- Mixed page sizes and orientations.
- Bookmarks and outlines.
- Annotations of different types.
- AcroForms and form fields.
- Password-protected PDFs.
- PDFs with existing digital signatures.
- Encrypted PDFs with varying supported settings.
- PDF attachments.
- Documents with external links and unsupported actions.
- Tagged and untagged PDFs.
- PDF/A examples with verified claims.
- Very large and resource-intensive documents.
- Malformed and truncated PDFs created for testing.
- Documents with metadata and hidden content relevant to redaction.
- Redaction canary documents: a unique marker string in text, images, vector art, shared form XObjects, annotations, metadata, attachments, scripts, bookmarks, hidden layers, and an earlier incremental revision.
- Signed documents with revisions added after signing, and samples of published signature attack classes.
- AcroForms with JavaScript calculations, and an XFA form to test detection.
- A 2,000-page document for rendering benchmarks.

Prefer synthetic fixtures produced by a generator script committed to the repository. Add third-party documents only with recorded provenance and licence.

Never place confidential user documents in public fixtures.

## 4. Required automated test categories

### Unit tests
Domain logic, page-range parsing, settings, command validation, state transitions, and error mapping.

### Integration tests
Engine opening, rendering, text extraction, saving, reopening, page operations, and OCR integration.

### Document regression tests
Expected page counts, extracted text, page geometry, annotation state, output integrity, and rendering comparisons.

### Security tests
Malformed inputs, resource exhaustion limits, unsafe external actions, path handling, temporary-file cleanup, and redaction verification.

### Platform tests
Build, package, launch, file picker, print workflow, fonts, keyboard behaviour, and file permissions on every supported operating system.

### Privacy tests
No unexpected network traffic during ordinary offline workflows; no passwords or document content in logs; predictable cache and temporary-file behaviour.

### Release tests
Clean checkout, reproducible documented build procedure, installer integrity, licence notices, SBOM, upgrade behaviour, and uninstall behaviour.

## 5. AI-generated code review checklist

Before merging AI-generated code, ask:

- Does the code satisfy the exact requirement?
- Are all error paths handled?
- Can a cancellation leave a partial or corrupted output?
- Does it block the UI?
- Are inputs validated?
- Does it cross a security boundary?
- Does it leak sensitive content through logs?
- Does it add a dependency?
- Does it introduce platform-specific assumptions?
- Are tests meaningful, or do they simply mirror the implementation?
- Are mocks being mistaken for real functionality?
- Are tests passing for the right reasons?
- Is the code unnecessarily complex?
- Does documentation accurately describe behaviour?
- Could the change break saved documents or compatibility?
- Has an independent reviewer examined the diff?

## 6. Initial issue backlog

### Foundation
- [ ] Create repository and governance files.
- [ ] Establish CI for Windows, macOS, and Linux.
- [ ] Run the two-week spike (`docs/06-SPIKE-PLAN.md`) and record its ADRs.
- [ ] Build the out-of-process PDFium worker.
- [ ] Integrate qpdf as the structural adapter.
- [ ] Approve initial dependency/licence inventory process.
- [ ] Create ADR templates.
- [ ] Establish a legal test-document corpus.
- [ ] Add formatting, static analysis, and dependency checks.

### Reader
- [ ] Open local PDF.
- [ ] Display page count.
- [ ] Render first page.
- [ ] Navigate pages.
- [ ] Zoom and scroll.
- [ ] Extract and search text.
- [ ] Handle password-protected files.
- [ ] Handle invalid files.
- [ ] Save settings and recent files locally.
- [ ] Verify offline behaviour.

### Reliability
- [ ] Add cancellation to long-running tasks.
- [ ] Test large documents.
- [ ] Add output-safe save workflow.
- [ ] Add crash and recovery tests.
- [ ] Establish rendering regression tests.
- [ ] Document supported and unsupported features.
- [ ] Set up the PDFium update process: check weekly, update at least monthly, and update out of band for High or Critical CVEs.

### Open-source readiness
- [ ] Add project licence after approval.
- [ ] Add contribution guide.
- [ ] Add code of conduct.
- [ ] Add security disclosure policy.
- [ ] Generate SBOM.
- [ ] Add third-party notices.
- [ ] Document reproducible builds.
- [ ] Add release checklist.
- [ ] Choose a new product name, and check trademarks, domains, and package IDs.
- [ ] Publish the AI-usage disclosure.

## 7. Definition of project success

Success is not the number of features listed on the website.

Success means:
- Users can trust the app with important documents.
- The application works on its declared platforms.
- Common workflows are reliable and understandable.
- The source code is inspectable and buildable.
- The project respects dependency licences.
- Security defects are investigated and corrected.
- Contributors can understand and extend the architecture.
- Limitations are disclosed honestly.
- No one has to pay to unlock core features.
