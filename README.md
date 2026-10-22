# PDF Suite (working name)

A free, open-source, cross-platform desktop PDF application for Windows, macOS, and Linux. It aims to be trustworthy first: verified redaction, honest signature status, offline by default, and an accessible reader. See `docs/01-PRD-AND-ARCHITECTURE.md`.

> **Status: technical spike.** Nothing here is a usable application yet. The current work is the two-week go/no-go spike described in `docs/06-SPIKE-PLAN.md`; results are recorded in `docs/research/spike-results.md`.
>
> "Alpha PDF Suite" is a temporary working name and will be replaced before any release.

## Build and test

Prerequisites: the .NET 10 SDK (version pinned in `global.json`).

Opt out of .NET SDK telemetry in your shell first. The repository itself disables the Avalonia build telemetry and the test-platform telemetry (see `docs/licensing/dependency-inventory.md` §3).

```bash
export DOTNET_CLI_TELEMETRY_OPTOUT=1
```

On Windows PowerShell, use `$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'` instead.

Then:

```bash
dotnet build PdfSuite.slnx
dotnet test --solution PdfSuite.slnx
dotnet run --project tools/PdfSuite.FixtureGenerator
dotnet run --project src/PdfSuite.Pdf.Worker -- --self-test fixtures/generated/large-2000-pages.pdf
dotnet run --project src/PdfSuite.App
```

The fixture generator writes synthetic test PDFs and `manifest.json` to `fixtures/generated/`, which is not committed. Output is byte-for-byte reproducible.

## Layout

| Path | Contents |
|---|---|
| `src/PdfSuite.Pdf.Abstractions` | Engine-neutral interfaces used by the application layers |
| `src/PdfSuite.Pdf.Pdfium` | PDFium bindings, the engine adapter, and an authoring API |
| `src/PdfSuite.Pdf.Worker` | The out-of-process engine host (self-test only during S0) |
| `src/PdfSuite.App` | The Avalonia desktop shell |
| `tools/PdfSuite.FixtureGenerator` | Synthetic, legally distributable test documents |
| `tests/PdfSuite.Spike.Tests` | Spike tests |
| `docs/` | PRD, roadmap, agent playbook, UI copy, checklists, spike plan, research, licensing |

## Licence

The licence for original project code is not yet adopted. Apache-2.0 is proposed (PRD §10). Third-party licences are recorded in `docs/licensing/dependency-inventory.md`.

## AI-assisted development

See `AI-USAGE.md`.
