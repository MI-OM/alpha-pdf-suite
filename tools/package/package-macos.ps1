<#
.SYNOPSIS
  Builds a .app bundle with the hardened runtime, in a DMG, from a published folder (spike S6).

.DESCRIPTION
  Every Mach-O file in the bundle is signed ad hoc ("-") with the hardened runtime. An ad hoc
  signature has no developer identity, so Gatekeeper rejects the app when it is downloaded with a
  browser (quarantined); it runs when copied by other means, as in CI. Notarisation needs a
  Developer ID certificate from the Apple Developer Program, which the spike does not have.
  NativeAOT code needs no JIT entitlement. PowerShell 7 on macOS, with Xcode's command-line tools.

.EXAMPLE
  pwsh tools/package/package-macos.ps1 -Layout artifacts/package/osx-arm64-aot/app -Output artifacts/package/pdf-suite-osx-arm64.dmg
#>
param(
    [Parameter(Mandatory)] [string] $Layout,
    [Parameter(Mandatory)] [string] $Output,
    [string] $Version = '0.1.0'
)

. (Join-Path $PSScriptRoot 'common.ps1')

$Output = [System.IO.Path]::GetFullPath($Output)
$staging = "$Output.staging"
if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
$bundle = Join-Path $staging 'PDF Suite.app'
$macOS = Join-Path $bundle 'Contents/MacOS'
$resources = Join-Path $bundle 'Contents/Resources'
New-Item -ItemType Directory -Force $macOS, $resources | Out-Null
Copy-Item -Recurse (Join-Path $Layout '*') $macOS

# The icon: sips scales the placeholder to each size, and iconutil assembles the .icns file.
$iconset = Join-Path $staging 'AppIcon.iconset'
New-Item -ItemType Directory -Force $iconset | Out-Null
$master = Join-Path $staging 'icon-1024.png'
Write-PlaceholderIcon 1024 $master
foreach ($size in 16, 32, 128, 256, 512) {
    Invoke-Native sips @('-z', $size, $size, $master, '--out', (Join-Path $iconset "icon_${size}x${size}.png")) | Out-Null
    Invoke-Native sips @('-z', (2 * $size), (2 * $size), $master, '--out', (Join-Path $iconset "icon_${size}x${size}@2x.png")) | Out-Null
}
Invoke-Native iconutil @('-c', 'icns', $iconset, '-o', (Join-Path $resources 'AppIcon.icns'))
Remove-Item -Recurse -Force $iconset, $master

$plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>$($PackageInfo.DisplayName)</string>
  <key>CFBundleDisplayName</key><string>$($PackageInfo.DisplayName)</string>
  <key>CFBundleIdentifier</key><string>$($PackageInfo.BundleId)</string>
  <key>CFBundleExecutable</key><string>PdfSuite.App</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$Version</string>
  <key>CFBundleVersion</key><string>$Version</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
"@
[System.IO.File]::WriteAllText((Join-Path $bundle 'Contents/Info.plist'), $plist)

# Sign the inside first (libraries and the worker), then the bundle, which signs the main
# executable and seals the resources.
foreach ($file in Get-ChildItem $macOS -File | Where-Object Name -ne 'PdfSuite.App') {
    if ((& file -b $file.FullName) -match 'Mach-O') {
        Invoke-Native codesign @('--force', '--options', 'runtime', '--timestamp=none', '--sign', '-', $file.FullName)
    }
}
Invoke-Native codesign @('--force', '--options', 'runtime', '--timestamp=none', '--sign', '-', $bundle)
Invoke-Native codesign @('--verify', '--deep', '--strict', '--verbose=2', $bundle)

# The DMG holds the app and a link to /Applications, the usual drag-to-install layout.
New-Item -ItemType SymbolicLink -Path (Join-Path $staging 'Applications') -Target '/Applications' | Out-Null
if (Test-Path $Output) { Remove-Item $Output }
Invoke-Native hdiutil @('create', '-volname', 'PDF Suite', '-srcfolder', $staging, '-format', 'UDZO', '-ov', $Output)
Remove-Item -Recurse -Force $staging

Write-Notice 'DMG' ("{0}: {1}, signed ad hoc with the hardened runtime; not notarised" -f (Split-Path $Output -Leaf), (Format-Megabytes (Get-Item $Output).Length))
