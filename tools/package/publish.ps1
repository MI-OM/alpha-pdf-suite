<#
.SYNOPSIS
  Publishes the app and the worker for one runtime identifier into one folder (spike S6).

.DESCRIPTION
  Modes:
    aot  NativeAOT: each program is one native executable with no JIT. Packages use this.
    r2r  Self-contained, with ReadyToRun code and the JIT. Kept to compare size and startup.
         It is not trimmed: the app and the worker share one copy of the runtime, and trimming
         each against its own code would produce two different versions of the same files.

  <Output>/app holds exactly what goes into a package. Symbol files (.pdb, .dbg, .dSYM) go to
  <Output>/symbols; SkiaSharp's and HarfBuzzSharp's alone are about 100 MB.
  Works in Windows PowerShell 5.1 and PowerShell 7.

.EXAMPLE
  pwsh tools/package/publish.ps1 -Rid linux-x64
  pwsh tools/package/publish.ps1 -Rid win-x64 -Mode r2r
#>
param(
    [Parameter(Mandatory)] [string] $Rid,
    [ValidateSet('aot', 'r2r')] [string] $Mode = 'aot',
    [string] $Output
)

. (Join-Path $PSScriptRoot 'common.ps1')
if (-not $Output) { $Output = Join-Path $RepoRoot "artifacts/package/$Rid-$Mode" }
$app = Join-Path $Output 'app'
$symbols = Join-Path $Output 'symbols'

& (Join-Path $RepoRoot 'tools/qpdf/fetch-qpdf.ps1') -Rid $Rid

# NativeAOT's Windows build finds the C++ linker with vswhere.exe, but expects vswhere on PATH.
# The Visual Studio installer does not put it there; the GitHub runner images do.
if ($OnWindows -and $Mode -eq 'aot') {
    $installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
    if (Test-Path (Join-Path $installer 'vswhere.exe')) { $env:PATH = "$env:PATH;$installer" }
}

foreach ($folder in $app, $symbols) {
    if (Test-Path $folder) { Remove-Item -Recurse -Force $folder }
}

$properties = if ($Mode -eq 'aot') { @('-p:PublishAot=true') } else { @('--self-contained', '-p:PublishReadyToRun=true') }

# The app first, then the worker. Publishing the app also copies the worker's framework-dependent
# build output (its project reference); the worker's own publish must overwrite that.
foreach ($project in 'src/PdfSuite.App', 'src/PdfSuite.Pdf.Worker') {
    Invoke-Native dotnet (@('publish', (Join-Path $RepoRoot $project), '-c', 'Release', '-r', $Rid, '-o', $app) + $properties)
}

if ($Mode -eq 'aot') {
    # Left over from the app's project reference to the worker; a native executable has no use for them.
    Get-ChildItem $app -File | Where-Object { $_.Name -like '*.runtimeconfig.json' -or $_.Name -like '*.deps.json' } | Remove-Item
}

New-Item -ItemType Directory -Force $symbols | Out-Null
Get-ChildItem $app | Where-Object { $_.Extension -in '.pdb', '.dbg', '.dSYM' } | Move-Item -Destination $symbols

$exe = if ($Rid -like 'win-*') { '.exe' } else { '' }
$required = @("PdfSuite.App$exe", "PdfSuite.Pdf.Worker$exe")
$required += switch -Wildcard ($Rid) {
    'win-*'   { 'pdfium.dll', 'qpdf*.dll' }
    'linux-*' { 'libpdfium.so', 'libqpdf.so*' }
    'osx-*'   { 'libpdfium.dylib', 'libqpdf*.dylib' }
}
foreach ($pattern in $required) {
    if (-not (Get-ChildItem $app -Filter $pattern -File)) { throw "The published folder has no $pattern." }
}

$size = Get-FolderSize $app
$symbolSize = Get-FolderSize $symbols
Write-Notice "Published $Rid ($Mode)" ("{0} in {1} files; symbols kept out of packages: {2}" -f (Format-Megabytes $size.Bytes), $size.Files, (Format-Megabytes $symbolSize.Bytes))
Get-ChildItem $app -File | Sort-Object Length -Descending | Select-Object -First 8 | ForEach-Object { '  {0,-40} {1}' -f $_.Name, (Format-Megabytes $_.Length) }
