# Security Policy

> **DRAFT for the maintainer to review before publishing.** The response times below are proposals, not yet commitments. Private vulnerability reporting must be enabled in the repository settings before this policy works as written.

PDF Suite (a working name) opens documents from untrusted sources. A PDF can try to exploit a parser, exhaust memory, hide content, or mislead the reader. Security problems are treated as release blockers (PRD §8).

## Supported versions

Nothing has been released. The repository holds technical-spike code, which is not meant for real documents. Once releases exist, this section will list which versions receive security fixes.

## Reporting a vulnerability

**Please do not open a public issue, discussion, or pull request for a security problem.**

Report it privately: on the repository's **Security** tab, choose **Report a vulnerability**. That opens a private advisory, visible only to you and the maintainers.

Please include:
- what is affected (component, file, or feature) and at which commit
- how to reproduce it: steps, and if possible a small test document
  - Build the document from scratch, or remove personal data from it. Never send a document that contains someone else's private information.
- what an attacker could achieve
- whether you plan to disclose it publicly, and when

**Proposed response times,** to be confirmed by the maintainer:
- acknowledge the report within 7 days
- give a first assessment within 14 days
- agree on disclosure timing with you; the aim is a fix and a public advisory within 90 days

Reporters are credited in the advisory unless they ask not to be.

## What is in scope

- **The application and its worker process:**
  - code execution, memory corruption or crashes triggered by a document
  - escaping the worker process (ADR-0003)
  - resource exhaustion the limits should have stopped
- **Redaction:** any way to recover redacted content from a file the redactor produced and its verifier accepted. Include the input file, the areas, and the output if you can.
- **Content that is acted on automatically:** document JavaScript, embedded files, launch actions, or external links that run or open without the user's action.
- **Privacy:** outbound network traffic the documentation does not declare (PRD §9), or document content written to logs.
- **Encryption and permissions** applied incorrectly, for example output that opens without the password.
- **Signature inspection** that reports a modified document as unmodified, once that feature exists.
- **The build and release pipeline:** dependency pinning, binary verification, and packaging.

## What is not a vulnerability here

- **Known limits, recorded in the spike results and the ADRs:**
  - **The worker is not yet sandboxed.** It runs with the user's privileges, and resource limits apply only on Windows (ADR-0003).
  - **Test builds are not trusted packages:** spike packages are signed with a throwaway test certificate (Windows) or ad hoc (macOS).
- **Bugs in PDFium or qpdf themselves:** report them upstream too. Do tell us if our use of them makes a bug exploitable or worse.
- **Documents that render differently from another viewer,** unless the difference hides or fakes content in a way that matters for security.

## How fixes are handled

- **PDFium updates** ship at least monthly, and out of band for High or Critical CVEs (PRD §8; ADR-0002).
- **Fixes come with regression tests.** For redaction, a fix also adds a canary fixture or a negative control to the verifier's tests (ADR-0006).
- **Security-sensitive code is reviewed independently before release:** document parsing, the worker protocol, redaction, encryption, and signature handling.
