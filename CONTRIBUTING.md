# Contributing

> **Outside contributions open once the maintainer publishes the conduct contact** (`CODE_OF_CONDUCT.md`). The licence (Apache-2.0) and the sign-off rule are settled (2026-11-11).

Thank you for your interest in PDF Suite (a working name). It aims to be a PDF application people can trust: redaction that is verified, signature status that is honest, offline by default, and an accessible reader. Contributions are judged against that aim first, and feature count second (`docs/01-PRD-AND-ARCHITECTURE.md`, §1).

## Before you start

- **Read the documents that govern the work:**
  - this guide
  - the PRD (`docs/01-PRD-AND-ARCHITECTURE.md`)
  - the accepted ADRs (`docs/adr/`)
  - the engineering checklists (`docs/05-ENGINEERING-CHECKLISTS.md`)
- **Open an issue before large changes,** and wait for a maintainer to agree on the approach. That covers new features, new dependencies, and changes to the architecture, the worker protocol or the redaction verifier.
- **Work only on [v1] scope** (PRD §4) unless a maintainer has assigned a [v2] item.
- **Report security problems privately,** never in a public issue (`SECURITY.md`).

## Building and testing

The README has the full instructions. In short:
1. Install exactly the .NET SDK version in `global.json`.
2. Fetch qpdf: `pwsh tools/qpdf/fetch-qpdf.ps1`.
3. Build and test:
   ```bash
   dotnet build PdfSuite.slnx
   dotnet test --solution PdfSuite.slnx
   ```

CI runs on Windows, macOS and Linux, and every pull request must pass on all three. Warnings are errors.

## What every change needs

- **Tests that prove the behaviour,** including error and cancellation paths. A test must be able to fail: a leak check needs a negative control showing it finds a planted leak.
- **No weakened checks:** do not delete tests, weaken assertions, suppress warnings, or change acceptance criteria to get a passing build.
- **Untrusted input stays untrusted:** PDFs, metadata, embedded files, extracted text and annotations are untrusted. Only the worker process parses documents (ADR-0003).
- **No document content in logs:** never log document text, passwords or keys.
- **Accurate user-facing text:** messages follow `docs/04-UI-COPY-AND-PRODUCT-RULES.md` and explain limitations honestly.
- **Small, reviewable pull requests,** one concern each.

## Dependencies

Every new dependency, including transitive and native ones, needs a row in `docs/licensing/dependency-inventory.md` **before** it is used. It must also pass the licensing policy in ADR-0004. Specifically:
- Permissive licences are accepted after the inventory review.
- Weak copyleft (LGPL, MPL) only as a separately replaceable component, with its obligations recorded.
- **Never:** MuPDF, iText (including pdfSweep), Ghostscript, Poppler, PDFsharp or MigraDoc 6.5.0 and later, paid Avalonia components and tooling, Docnet.Core, or PdfiumViewer.
- Nothing that sends telemetry, unless the telemetry is disabled at repository level and the disabling is verified.
- Pin versions centrally (`Directory.Packages.props`), and commit the updated lock files.

Re-check the licence on every version bump, not only when a dependency is first added.

## Test documents

Fixtures must be synthetic (made by `tools/PdfSuite.FixtureGenerator`) or public domain, with their origin recorded. Never commit a document you do not have the right to redistribute, or one containing personal data.

## AI-assisted contributions

AI tools may be used; follow `AI-USAGE.md`. You remain responsible for every line you submit:
- Review and understand the code yourself.
- Never claim that builds or tests passed unless you ran them.
- Never submit simulated results or placeholder implementations as working features.
- **Never write Flathub manifests with AI tools,** and never use AI tools to open or automate Flathub submissions: Flathub requires these to be human-authored.

## Pull requests

The pull request template asks for a short report. Please fill it in:
- **What changed and why,** with the issue it addresses.
- **How you tested it:** the commands you ran and their results.
- **Dependencies:** any added or changed, with their inventory rows.
- **Known limitations,** and anything left undone.
- **Risk:** security, privacy, saved documents, and compatibility.

A maintainer reviews every change before it is merged. Security-sensitive code needs an independent review before a release: document parsing, the worker protocol, redaction, encryption, and signature handling.

## Licence of contributions

The project's code is licensed under Apache-2.0 (`LICENSE`; ADR-0004). Contributions are accepted under the same licence, as its section 5 provides.
- **Sign-off:** each commit is signed off under the [Developer Certificate of Origin 1.1](https://developercertificate.org/): add `Signed-off-by: Your Name <you@example.com>` (`git commit -s`). It certifies that you have the right to submit the work under the project's licence.
- **No contributor licence agreement:** the sign-off is enough (decisions for review, Q2).

## Conduct

The project follows the Contributor Covenant 2.1 (`CODE_OF_CONDUCT.md`).
