<#
.SYNOPSIS
  Fetches qpdf's official prebuilt library for one platform and verifies it before use.

.DESCRIPTION
  Downloads the qpdf release archive for a runtime identifier (default: this machine) from
  qpdf's GitHub releases, checks it against the SHA-256 pinned below, and copies the shared
  library and its bundled dependencies to artifacts/qpdf/native/<rid>/. PdfSuite.Pdf.Qpdf copies
  that folder into build outputs. Works in Windows PowerShell 5.1 and PowerShell 7.

  The hashes come from qpdf's published checksum list for the release
  (https://github.com/qpdf/qpdf/releases/download/v12.4.2/qpdf-12.4.2.sha256, 2026-09-27).
  Change the version and all hashes together, and record the change in
  docs/licensing/dependency-inventory.md.

.EXAMPLE
  pwsh tools/qpdf/fetch-qpdf.ps1
  pwsh tools/qpdf/fetch-qpdf.ps1 -Rid linux-arm64
#>
param([string] $Rid)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$version = '12.4.2'
$assets = @{
    'win-x64'     = @{ File = "qpdf-$version-msvc64.zip";             Sha256 = 'db87077e683630c1217e0e8f9a20a9749d952ab676e881c3689187763a5de25d'; Dir = "qpdf-$version-msvc64/bin";     Pattern = '*.dll' }
    'win-arm64'   = @{ File = "qpdf-$version-msvc-arm64.zip";         Sha256 = '2b5d7740a2513d8a841116692dc2cf7efe83f35262386ebab18bed35fbf15ab1'; Dir = "qpdf-$version-msvc-arm64/bin"; Pattern = '*.dll' }
    'linux-x64'   = @{ File = "qpdf-$version-bin-linux-x86_64.zip";   Sha256 = 'db367d897829f22c4198ce1094143c9d467bd6ee7dfabc44ba6f02056b24f8b1'; Dir = 'lib';                          Pattern = '*.so*' }
    'linux-arm64' = @{ File = "qpdf-$version-bin-linux-aarch64.zip";  Sha256 = '8fd9d009eb0838398180a603f2cf76531e5c68a4ecc8847598e0a1ccb1d3b600'; Dir = 'lib';                          Pattern = '*.so*' }
    'osx-arm64'   = @{ File = "qpdf-$version-bin-macos-arm64.zip";    Sha256 = '62e46987a30ea167cbc530ccb22690aec3d8c812ed09979a941bcee92e504b79'; Dir = 'lib';                          Pattern = '*.dylib' }
    'osx-x64'     = @{ File = "qpdf-$version-bin-macos-x86_64.zip";   Sha256 = 'dd3b01f4414d198529bb0f13bba59c2f016a328bb3506695087a96fb80fc1481'; Dir = 'lib';                          Pattern = '*.dylib' }
}

if (-not $Rid) {
    $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    $os = if ($PSVersionTable.PSEdition -eq 'Desktop' -or $IsWindows) { 'win' } elseif ($IsMacOS) { 'osx' } else { 'linux' }
    $Rid = "$os-$arch"
}

$asset = $assets[$Rid]
if (-not $asset) { throw "No qpdf $version build is configured for '$Rid'. Known: $($assets.Keys -join ', ')" }

$repo = Split-Path (Split-Path $PSScriptRoot)
$downloads = Join-Path $repo 'artifacts/qpdf/downloads'
$extract = Join-Path $repo "artifacts/qpdf/extract/$Rid"
$native = Join-Path $repo "artifacts/qpdf/native/$Rid"
$marker = Join-Path $native 'qpdf-version.txt'

if ((Test-Path $marker) -and ((Get-Content $marker -Raw).Trim() -eq "$version $($asset.Sha256)")) {
    "qpdf $version for $Rid is already in $native"
    return
}

New-Item -ItemType Directory -Force $downloads | Out-Null
$archive = Join-Path $downloads $asset.File
if (-not (Test-Path $archive)) {
    "Downloading $($asset.File)"
    Invoke-WebRequest "https://github.com/qpdf/qpdf/releases/download/v$version/$($asset.File)" -OutFile $archive -UseBasicParsing
}

$actual = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $asset.Sha256) {
    Remove-Item $archive
    throw "Checksum mismatch for $($asset.File): expected $($asset.Sha256), got $actual. The download was deleted."
}

Remove-Item -Recurse -Force $extract, $native -ErrorAction SilentlyContinue
Expand-Archive $archive $extract
New-Item -ItemType Directory -Force $native | Out-Null

# Zip symlinks (libqpdf.so.30 -> libqpdf.so.30.4.2) extract as tiny text files; copy only real files.
Get-ChildItem (Join-Path $extract $asset.Dir) -Filter $asset.Pattern -File |
    Where-Object { $_.Length -gt 1024 } |
    ForEach-Object { Copy-Item $_.FullName $native }

Set-Content -Path $marker -Value "$version $($asset.Sha256)" -NoNewline
"qpdf $version for $Rid verified and copied to ${native}:"
Get-ChildItem $native | ForEach-Object { "  $($_.Name)" }
