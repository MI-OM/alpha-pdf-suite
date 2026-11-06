<#
.SYNOPSIS
  Builds an AppImage and a .deb from a published folder (spike S6).

.DESCRIPTION
  AppImage: appimagetool and the AppImage runtime are downloaded from their GitHub releases at the
  versions pinned below and checked against SHA-256 digests (from the GitHub release API,
  2026-11-01). The runtime becomes the first part of every AppImage, so its licences ship with it:
  MIT, plus statically linked musl, libfuse (LGPL-2.1), squashfuse, zstd and zlib
  (docs/licensing/dependency-inventory.md).

  .deb: installs to /opt/pdf-suite with a pdf-suite command and a desktop entry. The package
  declares the libraries the app loads at run time, so apt installs them on a minimal system, and
  the glibc version its files need, so apt refuses it on a system too old to run it.
  PowerShell 7 on Linux, with dpkg-deb.

.EXAMPLE
  pwsh tools/package/package-linux.ps1 -Layout artifacts/package/linux-x64-aot/app -OutputDirectory artifacts/package
#>
param(
    [Parameter(Mandatory)] [string] $Layout,
    [Parameter(Mandatory)] [string] $OutputDirectory,
    [string] $Version = '0.1.0',
    [ValidateSet('x64')] [string] $Architecture = 'x64'
)

. (Join-Path $PSScriptRoot 'common.ps1')

$tools = @{
    'appimagetool' = @{ Url = 'https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-x86_64.AppImage'; Sha256 = 'ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0' }
    'runtime'      = @{ Url = 'https://github.com/AppImage/type2-runtime/releases/download/20251108/runtime-x86_64'; Sha256 = '2fca8b443c92510f1483a883f60061ad09b46b978b2631c807cd873a47ec260d' }
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$work = Join-Path $OutputDirectory 'linux-staging'
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory -Force $work | Out-Null
$command = $PackageInfo.Command

$downloads = Join-Path $RepoRoot 'artifacts/appimage'
New-Item -ItemType Directory -Force $downloads | Out-Null
foreach ($name in $tools.Keys) {
    $file = Join-Path $downloads $name
    if (-not (Test-Path $file) -or (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $tools[$name].Sha256) {
        "Downloading $($tools[$name].Url)"
        Invoke-WebRequest $tools[$name].Url -OutFile $file
        $actual = (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $tools[$name].Sha256) {
            Remove-Item $file
            throw "Checksum mismatch for $name`: expected $($tools[$name].Sha256), got $actual. The download was deleted."
        }
    }
    Invoke-Native chmod @('+x', $file)
}

$desktopEntry = @"
[Desktop Entry]
Type=Application
Name=$($PackageInfo.DisplayName)
Comment=$($PackageInfo.Description)
Exec=$command %f
Icon=$command
Categories=Office;Viewer;
MimeType=application/pdf;
Terminal=false
"@

function Set-Modes([string] $Root) {
    # Packages carry the modes they are built with: 0755 for folders and programs, 0644 for the rest.
    Invoke-Native find @($Root, '-type', 'd', '-exec', 'chmod', '0755', '{}', '+')
    Invoke-Native find @($Root, '-type', 'f', '-exec', 'chmod', '0644', '{}', '+')
    foreach ($file in Get-ChildItem $Root -Recurse -File) {
        if ($file.Name -in 'PdfSuite.App', 'PdfSuite.Pdf.Worker', 'AppRun', $command) { Invoke-Native chmod @('0755', $file.FullName) }
    }
}

# --- AppImage ---
$appDir = Join-Path $work 'AppDir'
$appLib = Join-Path $appDir "usr/lib/$command"
New-Item -ItemType Directory -Force $appLib | Out-Null
Copy-Item -Recurse (Join-Path $Layout '*') $appLib
[System.IO.File]::WriteAllText((Join-Path $appDir "$command.desktop"), $desktopEntry)
Write-PlaceholderIcon 256 (Join-Path $appDir "$command.png")
[System.IO.File]::WriteAllText((Join-Path $appDir 'AppRun'), @"
#!/bin/sh
here="`$(dirname "`$(readlink -f "`$0")")"
exec "`$here/usr/lib/$command/PdfSuite.App" "`$@"
"@)
Set-Modes $appDir

$appImage = Join-Path $OutputDirectory "$command-linux-$Architecture.AppImage"
if (Test-Path $appImage) { Remove-Item $appImage }
# appimagetool is itself an AppImage; extracting and running it avoids needing FUSE on the build machine.
$env:APPIMAGE_EXTRACT_AND_RUN = '1'
$env:ARCH = 'x86_64'
Push-Location $work
try {
    Invoke-Native (Join-Path $downloads 'appimagetool') @('--no-appstream', '--runtime-file', (Join-Path $downloads 'runtime'), $appDir, $appImage)
}
finally {
    Pop-Location
    Remove-Item Env:APPIMAGE_EXTRACT_AND_RUN, Env:ARCH
}

# --- .deb ---
$root = Join-Path $work 'deb'
$opt = Join-Path $root "opt/$command"
New-Item -ItemType Directory -Force $opt, (Join-Path $root 'usr/bin'), (Join-Path $root 'usr/share/applications'), (Join-Path $root 'DEBIAN') | Out-Null
Copy-Item -Recurse (Join-Path $Layout '*') $opt
# A wrapper, not a symbolic link, so the app finds the worker and libraries next to its real path.
[System.IO.File]::WriteAllText((Join-Path $root "usr/bin/$command"), "#!/bin/sh`nexec /opt/$command/PdfSuite.App `"`$@`"`n")
[System.IO.File]::WriteAllText((Join-Path $root "usr/share/applications/$command.desktop"), $desktopEntry)
Write-PlaceholderIcon 256 (Join-Path $root "usr/share/icons/hicolor/256x256/apps/$command.png")
Set-Modes $root

# The newest glibc the files need becomes the libc6 version the package depends on, so apt refuses
# the package on an older system instead of installing one that cannot start (finding 53). The
# NativeAOT executables are linked on the build machine, so a newer build image can raise it. The
# libstdc++ and libgcc versions are recorded, not declared: the install jobs check them.
$floors = @(Get-RuntimeLibraryFloor $Layout)
foreach ($floor in $floors) {
    $symbols = ($floor.Symbols | Select-Object -First 12) -join ', '
    if ($floor.Symbols.Count -gt 12) { $symbols += " and $($floor.Symbols.Count - 12) more" }
    Write-Notice 'Library floor' ("{0}_{1}, needed by {2}, for {3}" -f $floor.Family, $floor.Version, ($floor.Files -join ', '), $symbols)
}
$glibc = $floors | Where-Object Family -eq 'GLIBC'
if (-not $glibc) { throw "No file in $Layout needs a glibc version, so the libc6 dependency cannot be set." }

# Libraries loaded at run time, which dpkg-shlibdeps cannot see: X11, ICE, SM and fontconfig
# (Avalonia), and ICU (.NET globalisation). The ICU packages are those of the releases the Package
# workflow installs on: Ubuntu 22.04 (70), 24.04 (74) and 26.04 (78), and Debian 12 (72) and 13 (76).
# Add a release there before adding its ICU here.
# Linked libraries, from ldd in the Package workflow: glibc (the executables need nothing else),
# fontconfig and FreeType (Skia), libstdc++, libgcc and zlib (qpdf), and GMP (qpdf's GnuTLS and Nettle).
$installedKb = [math]::Ceiling((Get-FolderSize $root).Bytes / 1KB)
$control = @"
Package: $command-spike
Version: $Version
Architecture: amd64
Maintainer: PDF Suite spike <maintainers@pdfsuite.invalid>
Installed-Size: $installedKb
Depends: libc6 (>= $($glibc.Version)), libgcc-s1, libstdc++6, zlib1g, libgmp10, libfreetype6, libicu78 | libicu76 | libicu74 | libicu72 | libicu70, libx11-6, libice6, libsm6, libfontconfig1
Section: text
Priority: optional
Description: PDF Suite technical spike build
 A test build of PDF Suite from its technical spike. Not a usable application.
"@
[System.IO.File]::WriteAllText((Join-Path $root 'DEBIAN/control'), $control + "`n")

$deb = Join-Path $OutputDirectory "$command-linux-$Architecture.deb"
Invoke-Native dpkg-deb @('--build', '--root-owner-group', $root, $deb)
Remove-Item -Recurse -Force $work

foreach ($package in $appImage, $deb) {
    Write-Notice 'Linux package' ("{0}: {1}" -f (Split-Path $package -Leaf), (Format-Megabytes (Get-Item $package).Length))
}
