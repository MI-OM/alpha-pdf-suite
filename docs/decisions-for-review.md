# Decisions for review

**Started:** 2026-11-07, when the maintainer asked the agent to work unattended, make decisions and leave them for review.
**Status of everything here:** provisional. Each decision can be reversed; none has been pushed or published. Where a decision needs money, a legal identity or a human tester, it stays open with a recommendation.

**Not legal advice.** Each decision notes the rules it was checked against. That is a careful reading of well-known requirements, not a legal opinion. Anything with legal weight should be confirmed by a qualified reviewer before release.

## What was built on 2026-11-07, for review

Eight local commits, none pushed (`git log origin/main..HEAD`). Each is one feature, with its tests and records. The details are in the spike results, "After the spike".

| Commit | Feature | Checked by |
|---|---|---|
| `0e8c296` | Password prompt, and this log | 2 tests; UI Automation |
| `049aff3` | Document Properties (Ctrl+D) | 17 tests; UI Automation |
| `ff80535` | Page navigation, Fit Width, Fit Page and Actual Size | UI Automation |
| `fbaa569` | Text selection and copy, honouring copy permissions | 7 tests; posted mouse messages; smoke test |
| `4874a20` | Close Document, and the open-close-reopen gate | 1 test; UI Automation |
| `99665d0` | Cmd for shortcuts on macOS | Not verifiable here |
| `f338569` | Document outline sidebar, safe against looping outlines | 4 tests; UI Automation |
| `623ce3c` | Recent documents, stored locally, clearable | UI Automation |

- **On Windows:**
  - All 129 tests ran: 126 passed, and the 3 explicit probes were skipped.
  - A local NativeAOT publish built with no warnings, passed the smoke test, and passed every UI check above.
- **Bugs found and fixed while checking:**
  - copied text lost from the clipboard when the app closed
  - fitted and navigated-to pages shown shifted next to wider pages
  - outline entries without accessible names, and drawn in no colour
  - two Avalonia menu flyout behaviours
- **New finding 55:** Avalonia's tree items lack UI Automation's expand and collapse pattern.
- **Pushed 2026-11-07 after review; CI at `adc9836`** (CI run 42442164701, Package run 42442164704): every job passed.
  - All 129 tests ran on each OS: 126 passed on Windows, 125 on macOS and Linux (which also skip the Windows-only print test).
  - Every package searched all 2,000 pages and selected the shown page's text (3,109 characters), through its NativeAOT build. The network check passed with both in the traced run, and the glibc floor stayed at 2.34.
- **Not verified:**
  - the new UI on macOS and Linux: CI checks the engine and worker features, search and selection there, not the dialogs, toolbar or sidebar
  - keyboard shortcuts, as no keystrokes were sent to the desktop
  - real screen readers
- **To push:** this machine's active GitHub account is `ININ-Org`, which cannot push to the repository. Push as `MI-OM`, or switch the default account first.

## Decided (provisional)

| # | Decision | Why | Rules checked |
|---|---|---|---|
| D1 | **Honour a document's "copy text" permission.** Copying is disabled for documents that forbid it, and the app says why. | The conservative reading of PDF permissions. Other viewers differ, but ignoring the flag gains little and could look like working around a protection measure | ISO 32000 permission flags (§7.6.4); anti-circumvention law: US 17 U.S.C. §1201, EU Directive 2001/29/EC Art. 6 |
| D2 | **A document password stays in memory only,** for as long as the document is open, so a crashed worker can reopen it. It is never logged, saved to disk or sent anywhere. | Needed to recover from crashes without asking again. The copy rules already say never to log or transmit it (§6) | GDPR data minimisation and storage limitation (Art. 5(1)(c), (e)); UI copy rules §6 |
| D3 | **Keep refusing documents whose pages cannot be read** (finding 51's open question), rather than showing the readable pages. | Refusing is the safe default until the reader milestones design partial display, with a clear warning | PRD §9 (no silent data problems) |
| D4 | **No layout-analysis dependency (PdfPig) for now.** Untagged pages keep PDFium's reading order (finding 16). | No measured need yet, and every dependency adds licence and update work. PdfPig is Apache-2.0, so adding it later stays possible | Licence compatibility (ADR-0004) |
| D5 | **Select All selects the text of the page in view, not the whole document.** | A 2,000-page document's text on the clipboard is slow and rarely wanted. Whole-document copy can come with export (Milestone 3) | — |
| D7 | **Zoom keys follow the common PDF reader convention:** Ctrl+0 Fit Page, Ctrl+1 Actual Size, Ctrl+2 Fit Width; Ctrl+G goes to a page, Ctrl+D shows Document Properties. Ctrl+0 used to mean actual size. | People bring these keys from other readers | WCAG 2.2 SC 2.1.1 (keyboard), SC 2.1.4 (no single-key shortcuts) |
| D8 | **Recent documents are remembered:** the last 10 file paths, in a JSON file in the user's application data folder. No contents or previews; never sent anywhere; Clear recent history deletes the file. Smoke tests and benchmarks add nothing. There is no setting to turn it off yet; the copy rules list one (§14). | Expected by readers, and the copy rules ask for it (§2, §3) | GDPR: purpose limitation, data minimisation and the user's control (Art. 5(1)(b), (c)); privacy by design (Art. 25). Stored on the device only |
| D6 | **Search and other messages the copy rules lacked** are written in the rules' style and listed in §5 of the UI copy rules as proposals. | The copy rules are the source of truth; new wording goes there for review, not only into code | UI copy rules §16 |

## Decided 2026-11-11, at the maintainer's instruction to take decisions

The maintainer asked the agent to "take decisions and document it clear". These close most of what was open. Each can be reversed.
- **Not yet published:** the licence becomes public only when these commits are pushed. Pushing it is the act that releases this code under Apache-2.0.

| # | Decision | Why | Rules checked |
|---|---|---|---|
| Q1 | **"Adobe quality" means a quality bar, not parity or likeness:** the PRD's definition of done, its measurable goals and the roadmap's gates (`docs/quality-bar.md`). | The PRD rules out claiming Acrobat parity, and the copy rules forbid imitating Adobe's identity | Trademark and passing-off law: no Adobe name, look or marks are used |
| Q2 | **Apache-2.0 for the project's own code** (`LICENSE`, verbatim from GitHub's licence API; `NOTICE`). Contributions are signed off under the Developer Certificate of Origin 1.1; no contributor licence agreement. The copyright line reads "The Alpha PDF Suite authors"; the maintainer may put their legal name instead. | Compatible with every bundled dependency: PDFium BSD-3-Clause, qpdf Apache-2.0, GnuTLS LGPL as a separate library, Avalonia, SkiaSharp and .NET MIT, the AppImage runtime MIT and LGPL. It gives contributors and users an explicit patent grant. The DCO is lighter for contributors than an agreement | Apache-2.0 §4 (notices) and §5 (contributions); LGPL-2.1 §6 for the separately linked GnuTLS and libfuse (relinking stays possible); the dependency inventory |
| Q3 | **Contributor Covenant 2.1** as the code of conduct (`CODE_OF_CONDUCT.md`), adopted by reference. | The most widely used open-source code. Adopting it by reference avoids an inexact copy | **Still needs the maintainer:** a contact for reports, published before outside contributions open |
| Q4 | **ADR-0002, 0003, 0004, 0005 and 0006 are accepted.** ADR-0001 stays Proposed. | The spike's evidence supports each as proposed. ADR-0001's own condition is a real screen-reader test, which is evidence, not judgement | Each ADR's licensing and security sections |
| Q5 | **The signing route** (ADR-0005 decision 4): the Microsoft Store first, then SignPath Foundation for direct Windows downloads; a Developer ID with notarisation for macOS; SHA-256 checksums for Linux. | Free routes first. Notarisation is the only way past Gatekeeper | **Carrying it out needs the maintainer:** joining the Apple Developer Program (USD 99 a year, a purchase the agent may not make) and applying to the Store and to SignPath under their own identity |
| Q6 | **Publish restores are locked** with a lock file per runtime, as ADR-0005 decision 5 proposed; CI restores publishes in locked mode. | Supply-chain integrity: a publish uses exactly the packages reviewed | ADR-0005 §6; the dependency inventory |

## Still open (needs the maintainer)

| Item | Why it cannot be decided here | Recommendation |
|---|---|---|
| Product name | Needs trademark searches in the markets of release, and an owner for the mark. The working name must change before release, as "AlphaPDF" is already a product (copy rules §1) | Shortlist three names, then search USPTO, EUIPO and WIPO's Global Brand Database, plus domains and package IDs |
| Conduct contact (Q3) | It is a person or address that receives reports | A dedicated address on the project's domain, once there is one |
| Apple Developer Program and Store and SignPath applications (Q5) | A purchase and applications under the maintainer's identity | As in Q5 |
| ADR-0001 | Waits on the NVDA and Narrator check (S2) | Run the S2 checklist in the spike results |
| AI-USAGE wording | It speaks for the maintainer | — |
| Human review of agent-written changes | PRD §12 needs a reviewer other than the implementing agent before anything is "done" | Review the commits since 2026-11-07, starting with this log |

## Compliance notes for features built on 2026-11-07

- **No new dependencies,** so no licence changes.
- **Privacy:** no telemetry, accounts or network use; the CI network check covers opening, rendering and searching. Passwords follow D2.
- **Accessibility:** new controls have names and keyboard access. The current search match is not shown by colour alone, status text is a live region, and dialogs can be used with the keyboard. These follow WCAG 2.2 level AA principles, which EN 301 549 (EU), Section 508 (US) and the European Accessibility Act refer to. Real screen-reader checks are still open.
- **Export controls:** the app's encryption comes from qpdf and its bundled GnuTLS, which are publicly available open-source cryptography. Under the US Export Administration Regulations (§742.15(b)), publicly available encryption source code using standard cryptography needs no notification since the 2021 rule change. Other countries have their own rules, and binary distribution through app stores has its own declarations (for example Apple's export compliance questions). To confirm before release.
- **Copyright:** copying honours the document's permission (D1).
