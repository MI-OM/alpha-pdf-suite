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

## Still open (needs the maintainer)

| Item | Why it cannot be decided here | Recommendation |
|---|---|---|
| Project licence (ADR-0004) | Choosing the licence for the project's own code is the copyright holder's legal act | Apache-2.0, as proposed. It is compatible with every bundled dependency (PDFium BSD-3-Clause, qpdf Apache-2.0, Avalonia MIT, SkiaSharp MIT, GnuTLS LGPL as a separate library, the AppImage runtime's MIT and LGPL parts) and gives contributors an explicit patent grant |
| Product name | Needs trademark searches in the markets of release, and an owner for the mark | Shortlist three names, then search USPTO, EUIPO and WIPO's Global Brand Database, plus domains and package IDs |
| Apple Developer Program | Costs USD 99 a year and needs a legal identity | Join before any macOS release: without notarisation, Gatekeeper blocks downloaded copies |
| Windows signing route | Needs an account, an identity check and possibly fees | The Microsoft Store first (it re-signs packages for free), then SignPath Foundation (free for open source) for direct downloads |
| Code of conduct | Needs a named contact who receives reports | Contributor Covenant 2.1, with a dedicated contact address |
| Accepting ADR-0001 to ADR-0006 | Accepting architecture is the maintainer's call | Accept ADR-0002, 0003, 0005 and 0006 as proposed. ADR-0001 waits on the NVDA and Narrator check; ADR-0004 waits on the licence |
| Locked publish restores (ADR-0005 decision 5) | The per-OS lock files can only be checked by CI, which runs after a push | Adopt per-runtime lock files, as ADR-0005 proposes, in a change verified by CI |
| AI-USAGE wording | It speaks for the maintainer | — |
| NVDA and Narrator check (S2) | Needs a person with a screen reader | Run the S2 checklist in the spike results |

## Compliance notes for features built on 2026-11-07

- **No new dependencies,** so no licence changes.
- **Privacy:** no telemetry, accounts or network use; the CI network check covers opening, rendering and searching. Passwords follow D2.
- **Accessibility:** new controls have names and keyboard access. The current search match is not shown by colour alone, status text is a live region, and dialogs can be used with the keyboard. These follow WCAG 2.2 level AA principles, which EN 301 549 (EU), Section 508 (US) and the European Accessibility Act refer to. Real screen-reader checks are still open.
- **Export controls:** the app's encryption comes from qpdf and its bundled GnuTLS, which are publicly available open-source cryptography. Under the US Export Administration Regulations (§742.15(b)), publicly available encryption source code using standard cryptography needs no notification since the 2021 rule change. Other countries have their own rules, and binary distribution through app stores has its own declarations (for example Apple's export compliance questions). To confirm before release.
- **Copyright:** copying honours the document's permission (D1).
