# The quality bar, and where the project stands

**Written:** 2026-11-11, when the maintainer asked to "continue until we are at the Adobe quality level".
**Decision Q1 (agent, for review):** "Adobe quality" is read as a quality bar, not as feature parity or likeness.
- **Not parity:** the PRD rules out claiming Acrobat parity (roadmap, "Initial release definition") and races no one on feature count (PRD §1).
- **Not likeness:** the copy rules forbid using Adobe's branding or imitating its product identity (§1).
- **So the bar is:** every feature v1 promises is as reliable, polished, accessible and correct as a professional reader's. The PRD calls this "professional-grade" and "verified trustworthiness" (§1), and it can be measured with the PRD's own tests below.

## The bar

A feature meets it only when all of these hold. They come from the project's own documents, so nothing here is new policy:
1. **Done, by PRD §12's definition:**
   - documented
   - no placeholders
   - unit, integration and regression tests
   - error and cancellation paths tested
   - accessibility and keyboard reviewed
   - security reviewed
   - licences recorded
   - honest copy
   - CI passing on every target OS
   - **approved by a reviewer other than the implementing agent**
2. **The measurable goals of PRD §11:** a usable window in under 3 s; the first page in under 2 s; no blocking at 25–800% zoom on a 2,000-page document; cancellation; no data loss in save and reopen cycles; no critical or high security issue.
3. **Its milestone's acceptance gate** in the roadmap (`docs/03-ROADMAP-AND-ACCEPTANCE.md`).
4. **Supported, not just implemented,** on each platform (feature matrix states): real screen readers, native dialogs, printing and keyboard conventions (PRD §6).

**What this means for agent-written work:** item 1's last point needs a person. So nothing the agent builds is "done" until the maintainer, or another human reviewer, approves it. Until then the feature matrix says "Prototyped".

## Where each milestone stands (2026-11-11)

| Milestone | Gate | State | What is missing for the bar |
|---|---|---|---|
| 0: Spike and governance | ADRs accepted, licence, policies | Spike done; governance being decided (decisions for review, Q2–Q6) | Maintainer approval of the decisions; the NVDA and Narrator check |
| 1: Reader prototype | Core reader, crashes, network | Every goal prototyped and tested on 3 OSes in CI | Rotation; single-page and facing-page modes; network check on Windows and macOS; S1 measurements on Linux and macOS; human review |
| 2: Reading, annotation, printing | Annotations survive save and reopen; printing on each OS; screen readers | Outline, recent files and search are prototyped | Save-a-copy workflow; annotations; thumbnails; tabs; macOS and Linux printing; screen-reader testing on 3 OSes |
| 3: Pages, creation, metadata | Page operations validated; no overwrites | Page operations in the engine and worker; rotate, delete, undo and Save a Copy in the app (2026-11-12) | Reorder, insert, extract and merge in the app (thumbnails); images to PDF; export; metadata editing; batch |
| 4: Forms, encryption, OCR | Forms round-trip; encryption in other viewers | Encryption in the worker (S4) | Forms; encryption UI; OCR (Tesseract); LibreOffice conversion |
| 5: Redaction, signatures, CLI | Canary tests; signature attack classes | Redaction method and verifier (S5) | Redaction UI; sanitisation; signature inspection; command line |
| 6: Hardening | Security review, packaging, PDFium currency | Packages install on every OS in CI | Signing and notarisation; independent security review; fuzzing the project's parsers |

## Order of work

Following the roadmap's priorities, P0 then P1 (roadmap, "Prioritization framework"):
1. **Governance decisions** that only need judgement (Q2–Q6), so the Milestone 0 gate can close once the maintainer confirms.
2. **Reproducible builds:** locked publish restores (ADR-0005 decision 5). This is supply-chain integrity, P0.
3. **Saving without data loss:** a save-a-copy workflow that writes to a temporary file and then replaces the target (copy rules §7). Every editing feature depends on it, and the PRD's "no data loss" goal is P0.
4. **Page operations** on top of it: rotate, delete, reorder, extract, insert and merge (Milestone 3). They are the most-used editing features, and testable without a screen.
5. **Thumbnails** for page navigation and operations (Milestone 2).
6. **Then:** annotations; macOS and Linux printing; tabs; forms, OCR and the rest, milestone by milestone.

Each step is committed on its own, with tests, its records and the feature matrix updated, and each decision written down in `docs/decisions-for-review.md`.
