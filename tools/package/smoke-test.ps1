<#
.SYNOPSIS
  Launches the app with --smoke-test several times and reports startup times (spike S6, Windows).

.DESCRIPTION
  Each run opens the fixture, waits until the first page is sharp, asks the worker for qpdf's
  version, and exits. A run passes only if the page rendered and qpdf loaded. On a fresh machine
  the first run is a cold start. Exits with 1 if any run fails. macOS and Linux use smoke-test.sh.
  Works in Windows PowerShell 5.1 and PowerShell 7.

.EXAMPLE
  powershell tools/package/smoke-test.ps1 -Label 'MSIX' -Command "$env:LOCALAPPDATA\Microsoft\WindowsApps\pdf-suite.exe" -Fixture fixtures/generated/large-2000-pages.pdf
#>
param(
    [Parameter(Mandatory)] [string] $Label,
    [Parameter(Mandatory)] [string] $Command,
    [Parameter(Mandatory)] [string] $Fixture,
    [int] $Runs = 3,
    [int] $TimeoutSeconds = 120
)

. (Join-Path $PSScriptRoot 'common.ps1')
$fixturePath = (Resolve-Path $Fixture).Path
if (Test-Path $Command) { $Command = (Resolve-Path $Command).Path }
$outputs = Join-Path ([System.IO.Path]::GetTempPath()) "pdf-suite-smoke-$PID"
New-Item -ItemType Directory -Force $outputs | Out-Null

$failed = 0
foreach ($run in 1..$Runs) {
    $out = Join-Path $outputs "run-$run.json"
    $process = Start-Process -FilePath $Command -ArgumentList "`"$fixturePath`"", '--smoke-test', '--out', "`"$out`"" -PassThru
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill()
        Write-Notice "Smoke test failed ($Label)" "Run $run did not finish within $TimeoutSeconds s."
        $failed++
        continue
    }

    if (-not (Test-Path $out)) {
        Write-Notice "Smoke test failed ($Label)" "Run $run exited with code $($process.ExitCode) and wrote no results."
        $failed++
        continue
    }

    Get-Content $out -Raw
    $result = Get-Content $out -Raw | ConvertFrom-Json
    $kind = if ($run -eq 1) { 'first run' } else { 'warm' }
    $summary = "run $run ($kind): exit $($process.ExitCode); $($result.environment.codeGeneration); window $($result.startup.processStartToWindowMs) ms; first page sharp $($result.startup.processStartToFirstPageSharpMs) ms; qpdf $($result.qpdfVersion)"
    if ($process.ExitCode -eq 0 -and $result.firstPageSharp -and $result.qpdfVersion) {
        Write-Notice "Smoke test ($Label)" $summary
    }
    else {
        Write-Notice "Smoke test failed ($Label)" "$summary; $($result.qpdfError)"
        $failed++
    }
}

if ($failed -gt 0) { exit 1 }
