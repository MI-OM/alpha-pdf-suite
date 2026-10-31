# PDF Suite (working name)

A free, open-source, cross-platform desktop PDF application for Windows, macOS, and Linux. It aims to be trustworthy first: verified redaction, honest signature status, offline by default, and an accessible reader. See `docs/01-PRD-AND-ARCHITECTURE.md`.

> **Status: technical spike.** Nothing here is a usable application yet. The current work is the two-week go/no-go spike described in `docs/06-SPIKE-PLAN.md`; results are recorded in `docs/research/spike-results.md`.
>
> "Alpha PDF Suite" is a temporary working name and will be replaced before any release.

## Build and test

Prerequisites: exactly the .NET SDK version pinned in `global.json` (`rollForward: disable`).

The SDK adds some packages itself, such as `Microsoft.NET.ILLink.Tasks`, at its own version. Those versions are recorded in the lock files, so any other SDK, even a newer patch, fails the locked restore. To move to a new SDK:
1. Install it.
2. Update `global.json`.
3. Run `dotnet restore PdfSuite.slnx --force-evaluate`.
4. Commit `global.json` and the lock files together.

Opt out of .NET SDK telemetry in your shell first. The repository itself disables the Avalonia build telemetry and the test-platform telemetry (see `docs/licensing/dependency-inventory.md` §3).

```bash
export DOTNET_CLI_TELEMETRY_OPTOUT=1
```

On Windows PowerShell, use `$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'` instead.

Fetch qpdf's native library once per machine. The script downloads the pinned official release and checks its SHA-256:

```bash
pwsh tools/qpdf/fetch-qpdf.ps1
```

Then:

```bash
dotnet build PdfSuite.slnx
dotnet test --solution PdfSuite.slnx
dotnet run --project tools/PdfSuite.FixtureGenerator
dotnet run --project src/PdfSuite.Pdf.Worker -- --self-test fixtures/generated/large-2000-pages.pdf
dotnet run --project src/PdfSuite.App
```

The fixture generator writes synthetic test PDFs and `manifest.json` to `fixtures/generated/`, which is not committed. Output is byte-for-byte reproducible on one OS; PDFium's output differs between OSes (spike results, finding 8).

The app opens a PDF given on the command line or through **Open PDF…**. Scroll with the wheel or Space, and zoom with Ctrl + wheel or Ctrl + plus/minus. ↓/↑ move a reading highlight line by line, PageDown/PageUp move by page, and Ctrl+Home/End jump to the first or last page. Screen readers announce each line (spike S2). **Print…** or Ctrl+P prints through the worker; this is Windows-only in the spike (S3). Every document is parsed in a separate worker process. To rerun the S1 measurements (the window must stay visible):

```bash
dotnet run --project src/PdfSuite.App -c Release -- fixtures/generated/large-2000-pages.pdf --benchmark --kill-worker
```

Options: `--tile 256|512`, `--software`, `--out results.json`. Results go to `artifacts/benchmarks/`.

## Layout

| Path | Contents |
|---|---|
| `src/PdfSuite.Pdf.Abstractions` | Engine-neutral interfaces used by the application layers |
| `src/PdfSuite.Pdf.Pdfium` | PDFium bindings, the engine adapter, and an authoring API |
| `src/PdfSuite.Pdf.Qpdf` | qpdf bindings: AES-256 encryption, metadata, full rewrites (spike S4) |
| `src/PdfSuite.Pdf.Remote` | The app-to-worker protocol, worker client, crash-restarting document session, and Windows Job Object |
| `src/PdfSuite.Pdf.Worker` | The out-of-process engine host (`--serve`), plus `--self-test` for CI |
| `src/PdfSuite.App` | The Avalonia desktop shell: tiled document view and S1 benchmark |
| `tools/PdfSuite.FixtureGenerator` | Synthetic, legally distributable test documents |
| `tests/PdfSuite.Spike.Tests` | Spike tests |
| `docs/` | PRD, roadmap, agent playbook, UI copy, checklists, spike plan, research, licensing |

## Licence

The licence for original project code is not yet adopted. Apache-2.0 is proposed (PRD §10). Third-party licences are recorded in `docs/licensing/dependency-inventory.md`.

## AI-assisted development

See `AI-USAGE.md`.
