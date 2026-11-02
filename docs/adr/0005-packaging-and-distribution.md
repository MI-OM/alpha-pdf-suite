# ADR-0005: Packaging and distribution channels

**Status:** Proposed (2026-11-01). Signing and notarisation depend on decisions listed in spike plan §9: the Apple Developer Program, and the Windows signing route.
**Decides:** how the app is compiled for release, which package formats ship on each OS, how they are signed, and how the build stays reproducible.

## 1. Context and constraints
- **The PRD's v1 packages** (§6):
  - Windows: MSIX through the Microsoft Store, plus a signed installer once a signing route is approved
  - macOS: a signed and notarised DMG
  - Linux: AppImage and .deb
- **Out of scope:** Flathub, until PDFium can be built from source there (PRD §5).
- **Startup targets** (§11): a usable window in under 3 s.
- **What a supported platform needs** (§6): a clean CI build, a native smoke test, and a documented package.
- **Tooling:** Avalonia's Parcel packager is commercial and excluded (ADR-0004).
- **Cross-compiling:** NativeAOT cannot cross-compile, so each OS builds its own packages.

## 2. Options considered
**Build mode:**
- **NativeAOT.**
- **Self-contained with ReadyToRun.**
- **Trimmed self-contained.**
- **Framework-dependent:** needs .NET installed, so not acceptable for end users.

**Formats:**
- **Windows:** MSIX, or an installer (MSI or Inno Setup).
- **macOS:** a DMG with a `.app` bundle, or a pkg.
- **Linux:** .deb, AppImage, RPM, Snap; Flatpak and Flathub later.

**Signing:**
- **Windows:** Microsoft Store re-signing, SignPath Foundation, an OV certificate, or Azure Artifact Signing (US and Canada individuals only).
- **macOS:** a Developer ID with notarisation ($99 a year), or ad hoc signing only.
- **Linux:** unsigned packages with published checksums, or a signed apt repository later.

**Locking publish restores** (finding 42):
- leave them unlocked
- declare every runtime identifier in the projects
- commit a lock file per runtime identifier

## 3. Evidence collected
All from spike item S6 (spike results: S6 evidence and findings 38–49):
- **NativeAOT builds both programs with no warnings,** with warnings as errors. The only change needed was reflection-free benchmark JSON (finding 38).
- **Every package installed on a fresh runner and launched the viewer** (Package workflow runs 41356497705 and 41357234415):
  - the MSIX on Windows: 22.4 MB, after trusting a test certificate
  - the DMG on macOS: 24.1 MB, signed ad hoc with the hardened runtime
  - the .deb in a bare Ubuntu 24.04 container: 18.4 MB
  - the AppImage on Ubuntu: 21.6 MB
  - Each launch rendered the 2,000-page fixture and loaded qpdf in the worker.
- **First launch after installing:** the window in 0.07–0.62 s and the first page in 0.15–2.4 s. The AppImage took 1.7–3.6 s to the window (findings 48 and 49).
- **NativeAOT against ReadyToRun:**
  - NativeAOT folders are 51–55 MB, against 133–143 MB for ReadyToRun.
  - ReadyToRun first runs took 3.6 to 12 times as long to the first page (findings 40 and 49).
- **A trimmed build cannot share one folder** between the app and the worker (finding 44).
- **Gatekeeper rejects the ad hoc signed app.** The app itself needs no entitlements under the hardened runtime (finding 46).
- **The AppImage runtime links libfuse (LGPL-2.1) statically** (finding 45).
- **The .deb's dependency list must come from `ldd`** plus the libraries loaded at run time (finding 47).
- **`PublishAot` in a project file makes the lock files OS-specific** (finding 41).

## 4. Decision and rationale
**Proposed:**
1. **Build mode:** NativeAOT for both programs, with `PublishAot` passed at publish time. `IsAotCompatible` keeps the analyzers on in every build. ReadyToRun stays only as a measured comparison.
2. **Packages for v1:**
   - **Windows x64:** MSIX. arm64 later.
   - **macOS arm64:** a DMG with a `.app` bundle. x64 is decided by build testing (PRD §6).
   - **Linux x64:** .deb and AppImage. arm64 later.
   - All are built by the project's own scripts in `tools/package/`, on each OS's CI runner.
3. **Every package is installed on a fresh machine and launched in CI** before it can be released. That is the Package workflow's install jobs.
4. **Signing:**
   - **Windows:** through the Microsoft Store first, which re-signs MSIX for free. A directly downloadable signed package waits for SignPath Foundation or an OV certificate.
   - **macOS:** a Developer ID and notarisation, which needs the Apple Developer Program. Until then, macOS builds are previews only and must say so.
   - **Linux:** packages are published with SHA-256 checksums. A signed apt repository can come later.
5. **Reproducible publishes:** commit a lock file per runtime identifier for the projects in the publish graph (about 15 small files), and restore publishes in locked mode. Declaring every runtime identifier instead would add three ILCompiler packages to every restore; the `win-x64` one alone is 36 MB.
6. **AppImage:** keep it, as the PRD lists it, provided the LGPL notice and source offer for its runtime are in place. Investigate its first-launch time (finding 48).

**Rationale:** NativeAOT gives the best startup and the smallest packages on every OS, and the S6 runs show every format installing and launching on clean machines.

## 5. Trade-offs
- **Build time and toolchains:** NativeAOT compilation takes 37–73 s per OS in CI, and needs each OS's C toolchain.
- **Reflection:** NativeAOT rules out reflection-based code (serialisers, dynamic loading). The analyzers enforce this, which constrains future dependencies.
- **Lock files:** 15 more of them to keep in step with the SDK pin.
- **macOS needs a paid membership.** Without it, Gatekeeper blocks downloaded copies.
- **The AppImage** brings LGPL obligations and slower first launches. The .deb has neither.

## 6. Licensing and security consequences
- **Licences that ship:**
  - **The NativeAOT runtime (MIT)** is compiled into both executables, so its third-party notices ship.
  - **The AppImage runtime** ships inside every AppImage, with MIT and LGPL-2.1 obligations.
  - **The MSVC runtime** ships under Microsoft's redistribution terms.
- **The packaging tools themselves do not ship:** appimagetool, makeappx, signtool, codesign, hdiutil, and dpkg-deb.
- **Supply chain:**
  - appimagetool and the AppImage runtime are pinned by SHA-256; the AppImage runtime's release signature is not yet verified.
  - Publish restores are not yet locked (decision 5 fixes this).
- **Signing:**
  - The spike's MSIX test certificate is created in memory on every run, is never stored, and is trusted only on throwaway CI runners. It must never be distributed or trusted on real machines.
  - An ad hoc signature gives no publisher identity. Only notarisation gives users a verifiable origin on macOS.

## 7. Reversibility and migration cost
- **The build mode** is a publish-time switch, so moving to ReadyToRun is a one-line change. A trimmed fallback would need the worker in its own folder, with its own runtime (finding 44).
- **Each OS's packaging** is one script of 80–140 lines. Adding or dropping a format touches one script and one workflow job.

## 8. Validation experiment
- **Done in the spike:** builds and installs on fresh runners, twice, on all three OSes.
- **Not done yet:**
  - notarisation with a Developer ID
  - a Microsoft Store submission
  - the per-runtime-identifier lock files
  - the AppImage on a real desktop, and its first-launch cost
  - arm64 packages
  - a Linux desktop session other than Xvfb
  - an upgrade from one package version to the next

## 9. Conditions that would cause reconsideration
- **A dependency needs reflection or runtime code generation** that NativeAOT cannot support.
- **A store or distribution channel** requires a different format, or forbids NativeAOT executables.
- **The maintainer decides not to join the Apple Developer Program.** macOS would then ship as an unsigned preview, or through Homebrew with its limits.
- **The AppImage's LGPL obligations or first-launch time** are judged not worth it. Ship .deb only, and Flatpak once Flathub becomes possible.
