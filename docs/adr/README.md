# Architecture Decision Records

Each significant technical choice gets an ADR (PRD §7.1). The format is set by the agent playbook (§3, "Required ADR format"), and `0000-template.md` follows it.

- **Status values:** Proposed, Accepted, Superseded by ADR-NNNN, Rejected.
- **Who decides:** a maintainer accepts an ADR. An agent may write and propose ADRs, but never accepts them (playbook §1).
- **Changes:** to change an accepted decision, write a new ADR that supersedes it. Do not rewrite the old one.

| ADR | Decision | Status | Evidence |
|---|---|---|---|
| [0001](0001-ui-framework-dotnet-and-printing.md) | UI framework, .NET version, and printing approach | Proposed; acceptance waits on the S2 screen-reader checks | Spike S1, S2, S3, S6 |
| [0002](0002-pdf-engine-strategy.md) | PDF engine strategy: PDFium, qpdf, and an in-house writer later | Proposed | Spike S0, S1, S3, S4, S5 |
| [0003](0003-engine-process-isolation-and-ipc.md) | Engine process isolation and IPC | Proposed | Spike S1, S3, S4, S5, S6 |
| [0004](0004-licensing-policy.md) | Licensing policy and excluded dependencies | Proposed | Viability assessment §3, dependency inventory |
| [0005](0005-packaging-and-distribution.md) | Packaging and distribution channels | Proposed | Spike S6 |
| [0006](0006-v1-redaction-method.md) | v1 redaction method and verification | Proposed | Spike S5 |

All six were proposed on 2026-11-01 at the end of the technical spike. Findings are cited by number from `docs/research/spike-results.md`.
