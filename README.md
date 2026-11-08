# PDF Suite (working name)

A free, open-source, cross-platform desktop PDF application for Windows, macOS, and Linux. It aims to be trustworthy first: verified redaction, honest signature status, offline by default, and an accessible reader. See `docs/01-PRD-AND-ARCHITECTURE.md`.

> **Status: technical spike, finished; decisions pending.** Nothing here is a usable application yet. The go/no-go spike in `docs/06-SPIKE-PLAN.md` has run. Its results and go/no-go outcomes are in `docs/research/spike-results.md`, and the decisions it proposes are in `docs/adr/`, waiting for review.
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

The app opens a PDF given on the command line or through **Open PDF…**. Scroll with the wheel or Space, and zoom with Ctrl + wheel or Ctrl + plus/minus. The second toolbar row goes to the previous, next or a typed page (Ctrl+G), and zooms: Fit Width (Ctrl+2), Fit Page (Ctrl+0) and Actual Size (Ctrl+1). ↓/↑ move a reading highlight line by line, PageDown/PageUp move by page, and Ctrl+Home/End jump to the first or last page. Screen readers announce each line (spike S2). **Search** or Ctrl+F opens the search bar: Enter or F3 finds the next match, Shift+Enter or Shift+F3 the previous one, and Escape closes it, leaving the reading position on the match's line. **Document Properties** or Ctrl+D shows what the document says about itself. A password-protected document asks for its password. **Print…** or Ctrl+P prints through the worker; this is Windows-only in the spike (S3). Every document is parsed in a separate worker process. To rerun the S1 measurements (the window must stay visible):

```bash
dotnet run --project src/PdfSuite.App -c Release -- fixtures/generated/large-2000-pages.pdf --benchmark --kill-worker
```

Options: `--tile 256|512`, `--software`, `--out results.json`. Results go to `artifacts/benchmarks/`.

### Packages (spike S6)

`tools/package/` builds installable packages from a NativeAOT publish. Each packaging script runs on its own OS:

```bash
pwsh tools/package/publish.ps1 -Rid win-x64
pwsh tools/package/package-windows.ps1 -Layout artifacts/package/win-x64-aot/app -Output artifacts/package/pdf-suite-win-x64.msix
pwsh tools/package/package-macos.ps1 -Layout artifacts/package/osx-arm64-aot/app -Output artifacts/package/pdf-suite-osx-arm64.dmg
pwsh tools/package/package-linux.ps1 -Layout artifacts/package/linux-x64-aot/app -OutputDirectory artifacts/package
```

- **Toolchain:** NativeAOT needs the platform's C and C++ toolchain: Visual Studio's C++ build tools on Windows, Xcode's command-line tools on macOS, and `clang` and `zlib1g-dev` on Linux.
- **Comparison build:** `publish.ps1 -Mode r2r` makes a self-contained ReadyToRun build, which is only used to compare size and startup.
- **Signing:** the MSIX is signed with a throwaway test certificate. The DMG is signed ad hoc and not notarised. So neither installs as a trusted app on an ordinary machine.
- **Smoke test:** `--smoke-test` (with `--out results.json`) opens the file, waits for the first page, and asks the worker for qpdf's version. It exits with 0 only if both work. `tools/package/smoke-test.ps1` (Windows) and `smoke-test.sh` (macOS, Linux) run it several times and report startup times.
- **CI:** the Package workflow builds all of this, then installs each package on a fresh runner and launches it.

## Layout

| Path | Contents |
|---|---|
| `src/PdfSuite.Pdf.Abstractions` | Engine-neutral interfaces used by the application layers |
| `src/PdfSuite.Pdf.Pdfium` | PDFium bindings, the engine adapter, and an authoring API |
| `src/PdfSuite.Pdf.Qpdf` | qpdf bindings: AES-256 encryption, metadata, full rewrites (spike S4), and object-level editing |
| `src/PdfSuite.Pdf.Redaction` | Redaction by rebuilding the document, and an independent verifier (spike S5) |
| `src/PdfSuite.Pdf.Remote` | The app-to-worker protocol, worker client, crash-restarting document session, and Windows Job Object |
| `src/PdfSuite.Pdf.Worker` | The out-of-process engine host (`--serve`), plus `--self-test` for CI. It parses documents with PDFium and qpdf, and runs redaction and qpdf operations |
| `src/PdfSuite.App` | The Avalonia desktop shell: tiled document view and S1 benchmark |
| `tools/PdfSuite.FixtureGenerator` | Synthetic, legally distributable test documents |
| `tools/package` | NativeAOT publish, MSIX, DMG, AppImage and .deb packaging, and the launch smoke test (spike S6) |
| `tests/PdfSuite.Spike.Tests` | Spike tests |
| `docs/` | PRD, roadmap, agent playbook, UI copy, checklists, spike plan, feature matrix, ADRs, research, licensing |

## What works today

`docs/07-FEATURE-MATRIX.md` lists every planned capability, its target release, and its status today, including what does not work yet.

## Contributing and security

Outside contributions cannot be accepted until the project licence is adopted. `CONTRIBUTING.md` (a draft) describes how contributions will work.

Report security problems privately, as `SECURITY.md` describes, never in a public issue.

## Licence

The licence for original project code is not yet adopted. Apache-2.0 is proposed (PRD §10). Third-party licences are recorded in `docs/licensing/dependency-inventory.md`.

## AI-assisted development

See `AI-USAGE.md`.
