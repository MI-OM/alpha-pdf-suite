# Dependency and Licence Inventory

**Status:** Spike (S0), recorded 2026-10-21. Not yet reviewed or approved.
**Source of truth for versions:** `Directory.Packages.props` and each project's `packages.lock.json`.
**Rule:** re-check every licence on every version bump (checklists §2). This is engineering record-keeping, not legal advice.

## 1. Direct dependencies

| Package | Version | Licence (nuspec) | Upstream | Used by | Ships in app? | Native binaries | Contacts network? | Replaceable? |
|---|---|---|---|---|---|---|---|---|
| bblanchon.PDFium.Win32 | 158.0.8111 | Apache-2.0 (packaging); PDFium itself BSD-3-Clause / Apache-2.0 | github.com/bblanchon/pdfium-binaries (commit 5b4d6b2) | Pdf.Pdfium, Worker | Yes | `pdfium.dll` for win-x86/x64/arm64 (6.9–7.6 MB) | No | Yes: engine adapter |
| bblanchon.PDFium.Linux | 158.0.8111 | As above | As above | As above | Yes | `libpdfium.so` for linux-x64/arm64/arm/x86 and musl variants (6–18 MB) | No | Yes |
| bblanchon.PDFium.macOS | 158.0.8111 | As above | As above | As above | Yes | `libpdfium.dylib` for osx-x64/arm64 (7.4–7.8 MB) | No | Yes |
| Avalonia | 12.1.4 | MIT | github.com/AvaloniaUI/Avalonia | App | Yes | Through SkiaSharp, HarfBuzzSharp, ANGLE | **Build time only, via Avalonia.BuildServices (disabled; §3)** | Costly: UI framework (ADR-0001) |
| Avalonia.Desktop | 12.1.4 | MIT | As above | App | Yes | — | — | — |
| Avalonia.Themes.Fluent | 12.1.4 | MIT | As above | App | Yes | — | — | — |
| xunit.v3 | 4.0.2 | Apache-2.0 | github.com/xunit/xunit | Tests only | No | — | **Via Microsoft.Testing.Extensions.Telemetry (disabled; §3)** | Yes |

### Native libraries fetched outside NuGet

| Library | Version | Licence | Source and verification | Used by | Bundled with it | Contacts network? |
|---|---|---|---|---|---|---|
| qpdf | 12.4.2 | Apache-2.0 | Official release archives from github.com/qpdf/qpdf, fetched by `tools/qpdf/fetch-qpdf.ps1`. Checked against SHA-256 values pinned in the script, copied from qpdf's published `qpdf-12.4.2.sha256`. The Sigstore signature on that list is not yet verified | `PdfSuite.Pdf.Qpdf`: the tests, redaction, and the worker, which ships it in packages from S6 | See the next table | No |

What each platform's archive brings, and what it obliges us to do:

| Platform | Files shipped | Licences | Obligations and notes |
|---|---|---|---|
| Windows x64 and arm64 | `qpdf30.dll`, with OpenSSL linked statically. Also the Microsoft C++ runtime: `vcruntime140*.dll`, `msvcp140*.dll`, `concrt140.dll` | Apache-2.0 (qpdf, OpenSSL 3); Microsoft Visual C++ Redistributable terms | Keep the notices. The MSVC runtime files may be redistributed under Microsoft's terms. Crypto providers: `openssl` and `native` |
| Linux x64 and arm64 | `libqpdf.so.30.4.2`, plus GnuTLS, Nettle and Hogweed, libtasn1, libunistring, libidn2, p11-kit, libffi, libjpeg | GnuTLS, libtasn1: **LGPL-2.1+**. Nettle, Hogweed, libunistring, libidn2: **LGPL-3+ or GPL-2+**. p11-kit: BSD-3-Clause. libffi: MIT. libjpeg: IJG | **LGPL obligations for distribution:** offer the source of those libraries, and let users replace them (dynamic linking satisfies this). That is compatible with Apache-2.0, but it is release work. **Alternative:** build qpdf with only its own `native` crypto provider, which removes GnuTLS and its dependencies. Decide this with the Flathub source-build question (S1 finding, S3 finding 21) |
| macOS arm64 and x64 | `libqpdf.30.4.2.dylib` | Apache-2.0 | No bundled third-party libraries |

Not used: the NuGet packages `QPdf.RuntimeLibraries` (a community package, stuck on qpdf 11.9.1) and `QPdfNet` (a wrapper around the command-line tool).

Not used, on purpose:
- **PDFiumCore.** The project owns its bindings instead (`src/PdfSuite.Pdf.Pdfium/Native/PdfiumNative.cs`).
  - PDFiumCore trails the native packages by one build.
  - Its obsolete `4688.0.0` release sorts as the "latest" version, which is a pinning trap.
  - Source-generated `LibraryImport` bindings are smaller and NativeAOT-friendly.
- **Microsoft.NET.Test.Sdk and xunit.runner.visualstudio.** xunit.v3 4.x runs on Microsoft.Testing.Platform (enabled in `global.json`), so these VSTest adapters are not needed.
- **The `bblanchon.PDFium` metapackage.** It also pulls Android, iOS, and WebAssembly binaries.

### Packaging tools (spike S6)

| Tool | Version | Licence | Source and verification | Ships in packages? | Notes |
|---|---|---|---|---|---|
| NativeAOT compiler and runtime: `Microsoft.DotNet.ILCompiler` and `runtime.<rid>.Microsoft.DotNet.ILCompiler` | 10.0.13, set by the pinned SDK | MIT | nuget.org, restored by `dotnet publish -p:PublishAot=true`. **Not in the committed lock files** (spike results, finding 42) | **Yes.** The .NET runtime is compiled into both executables | The .NET runtime's third-party notices must ship with the app (§4) |
| AppImage runtime (`type2-runtime`) | 20251108 | MIT, with statically linked musl (MIT), **libfuse (LGPL-2.1)**, squashfuse (BSD-2-Clause), zstd (BSD-3-Clause) and zlib (zlib licence), per the release's `LICENSE` | Release asset `runtime-x86_64` from github.com/AppImage/type2-runtime. SHA-256 pinned in `tools/package/package-linux.ps1`, from the GitHub release API. The release's own signature (`.sig`) is not yet verified | **Yes.** It is the first part of every AppImage | **LGPL-2.1 obligations for the AppImage:** ship the notices, and offer libfuse's source and the means to relink |
| appimagetool | 1.9.1 | MIT | Release asset from github.com/AppImage/appimagetool, SHA-256 pinned in the same script | No | Runs with `APPIMAGE_EXTRACT_AND_RUN=1`, so the build machine needs no FUSE |
| Windows SDK `makeappx.exe` and `signtool.exe` | 10.0.26100 | Windows SDK licence terms | Visual Studio installer (locally), runner image (CI) | No | — |
| C and C++ linkers for NativeAOT | MSVC from Visual Studio Build Tools 2026 (locally); runner images (CI); clang from Ubuntu | Microsoft; Apache-2.0 with LLVM exception | Visual Studio installer, Xcode on the runner, apt | No | Libraries linked into the executables are listed by the Package workflow (`ldd`, `otool -L`) |
| `dpkg-deb`, `hdiutil`, `codesign`, `sips`, `iconutil` | OS versions | OS tools | Runner images | No | — |

The packages also ship a placeholder icon drawn by `tools/package/common.ps1`. It contains no third-party artwork.

## 2. Transitive dependencies

| Package | Version | Licence | Pulled in by | Notes |
|---|---|---|---|---|
| Avalonia.Win32, .X11, .Native, .FreeDesktop, .FreeDesktop.AtSpi, .Skia, .HarfBuzz, .Remote.Protocol | 12.1.4 | MIT | Avalonia.Desktop | `.FreeDesktop.AtSpi` is the Linux screen-reader backend (spike S2) |
| Avalonia.BuildServices | 11.3.2 | MIT | Avalonia | **Build-time telemetry; disabled (§3)** |
| Avalonia.Angle.Windows.Natives | 2.1.27548.20260419 | BSD-3-Clause (ANGLE; LICENSE file in package) | Avalonia.Win32 | Native DirectX→GLES layer on Windows |
| SkiaSharp (+ NativeAssets.Win32/Linux/macOS/WebAssembly) | 3.119.4 | MIT | Avalonia.Skia | Native Skia binaries. WebAssembly assets are unused |
| HarfBuzzSharp (+ NativeAssets.*) | 8.3.1.3 | MIT | Avalonia.HarfBuzz | Native HarfBuzz binaries |
| MicroCom.Runtime | 0.11.6 | MIT | Avalonia | — |
| Tmds.DBus.Protocol | 0.94.1 | MIT | Avalonia.FreeDesktop | Linux D-Bus (portals, accessibility) |
| Microsoft.NET.ILLink.Tasks | 10.0.13 | MIT | `IsAotCompatible` (now also on the App) | Build-time trimming and AOT analyzers only |
| xunit.v3.* , xunit.analyzers | 4.0.2 / 2.2.0 | Apache-2.0 | xunit.v3 | Tests only |
| Microsoft.Testing.Platform, .MSBuild, Extensions.TrxReport.Abstractions | 2.5.1 | MIT | xunit.v3.core.mtp-v2 | Tests only |
| Microsoft.Testing.Extensions.Telemetry | 2.5.1 | MIT | xunit.v3.core.mtp-v2 | **Telemetry; hook removed (§3)** |
| Microsoft.ApplicationInsights | 2.23.0 | MIT | Microsoft.Testing.Extensions.Telemetry | Tests only; inert once the hook is removed |
| Microsoft.Bcl.AsyncInterfaces, Microsoft.Win32.Registry, System.Security.AccessControl | 6.0.0 / 5.0.0 / 6.0.1 | MIT | Test platform | Tests only |

NuGet audit (`NuGetAuditMode=all`, warnings treated as errors) reported no known vulnerabilities at restore on 2026-10-21.

## 3. Telemetry found in dependencies, and how it is disabled

| Component | What it does | Endpoint | Disabled by | Verified |
|---|---|---|---|---|
| Avalonia.BuildServices | Sends anonymous build data on every build of an Avalonia project: hashed project and machine names, OS, versions, CI detection | `av-build-tel-api-v1.avaloniaui.net/api/usage` | `Directory.Build.targets` replaces its `AvaloniaStats` target with an empty one. CI also sets `AVALONIA_TELEMETRY_OPTOUT=1`. | Detailed build log shows the override and 0 runs of `AvaloniaStatsTask` |
| Microsoft.Testing.Extensions.Telemetry | Sends test-run telemetry | `dc.services.visualstudio.com` | The test project removes its `TestingPlatformBuilderHook` item. CI also sets `DOTNET_CLI_TELEMETRY_OPTOUT=1` and `TESTINGPLATFORM_TELEMETRY_OPTOUT=1`. | Generated `SelfRegisteredExtensions.cs` registers only the MSBuild hook |
| .NET SDK CLI | Sends SDK usage telemetry | Microsoft | `DOTNET_CLI_TELEMETRY_OPTOUT=1`. Set this in your own environment. | — |

**Incident note:** before these controls existed, the App project was built about four times on the development machine without the Avalonia opt-out. Those builds probably sent Avalonia's anonymous build data.

## 4. Open items before any release

- [ ] **PDFium third-party notices.** The NuGet packages contain no licence or notice files.
  - Take PDFium's licence and the notices for its bundled libraries from the matching `chromium/8111` release archive on GitHub. The libraries include FreeType, libjpeg-turbo, OpenJPEG, ICU, zlib, Little CMS, libpng, and others.
  - Ship them in the app's third-party notices.
  - Confirm the exact list per platform.
- [ ] Note that the PDFium binaries are built without PartitionAlloc (viability assessment §3.3), and keep the worker isolation mandatory.
- [ ] SkiaSharp and HarfBuzzSharp native notices: collect them for the About screen and notices file.
- [ ] The .NET runtime's third-party notices (`THIRD-PARTY-NOTICES.TXT` in dotnet/runtime), which NativeAOT compiles into both executables.
- [ ] AppImage: the LGPL-2.1 notice and source offer for libfuse in the runtime, or no AppImage. Verify the runtime's release signature.
- [ ] Lock the packages that publishes restore for each runtime identifier, or record why not (spike results, finding 42).
- [ ] Pin GitHub Actions to commit SHAs instead of major-version tags.
- [ ] Reviewer and approval date for every row above.
