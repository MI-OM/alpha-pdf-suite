# Decisions for review

**Started:** 2026-11-07, when the maintainer asked the agent to work unattended, make decisions and leave them for review.
**Status of everything here:** provisional. Each decision can be reversed; none has been pushed or published. Where a decision needs money, a legal identity or a human tester, it stays open with a recommendation.

**Not legal advice.** Each decision notes the rules it was checked against. That is a careful reading of well-known requirements, not a legal opinion. Anything with legal weight should be confirmed by a qualified reviewer before release.

## Decided (provisional)

| # | Decision | Why | Rules checked |
|---|---|---|---|
| D1 | **Honour a document's "copy text" permission.** Copying is disabled for documents that forbid it, and the app says why. | The conservative reading of PDF permissions. Other viewers differ, but ignoring the flag gains little and could look like working around a protection measure | ISO 32000 permission flags (§7.6.4); anti-circumvention law: US 17 U.S.C. §1201, EU Directive 2001/29/EC Art. 6 |
| D2 | **A document password stays in memory only,** for as long as the document is open, so a crashed worker can reopen it. It is never logged, saved to disk or sent anywhere. | Needed to recover from crashes without asking again. The copy rules already say never to log or transmit it (§6) | GDPR data minimisation and storage limitation (Art. 5(1)(c), (e)); UI copy rules §6 |
| D3 | **Keep refusing documents whose pages cannot be read** (finding 51's open question), rather than showing the readable pages. | Refusing is the safe default until the reader milestones design partial display, with a clear warning | PRD §9 (no silent data problems) |
| D4 | **No layout-analysis dependency (PdfPig) for now.** Untagged pages keep PDFium's reading order (finding 16). | No measured need yet, and every dependency adds licence and update work. PdfPig is Apache-2.0, so adding it later stays possible | Licence compatibility (ADR-0004) |
| D5 | **Select All selects the text of the page in view, not the whole document.** | A 2,000-page document's text on the clipboard is slow and rarely wanted. Whole-document copy can come with export (Milestone 3) | — |
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
