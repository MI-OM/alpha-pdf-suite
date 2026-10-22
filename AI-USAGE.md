# AI Usage (interim disclosure)

> **DRAFT for the maintainer to review before publishing.** Wording is an open decision (spike plan §9). Edit anything that does not match how you actually work.

This project is developed with substantial help from AI coding agents, including Claude Code. AI tools are used for research, planning documents, code, tests, and build configuration.

How that work is controlled:

- A human maintainer reviews AI-generated changes before they are merged.
- The rules in `docs/02-AI-AGENT-PLAYBOOK.md` apply to every agent. They include:
  - no simulated results
  - no unverified claims of passing tests
  - no new dependencies without a licence record
- Security-sensitive code receives independent review before release. This includes redaction, signature handling, and document parsing.
- Packaging manifests submitted to Flathub are written by a human, and AI tools do not open or automate Flathub submissions, as Flathub's policy requires.

A fuller policy, including how AI involvement is marked in commits, will replace this note during Milestone 0.
