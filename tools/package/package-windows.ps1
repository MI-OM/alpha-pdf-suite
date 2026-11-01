<#
.SYNOPSIS
  Builds a signed MSIX from a published folder (spike S6).

.DESCRIPTION
  Writes the package manifest and placeholder logos, packs the folder with makeappx, and signs
  the package with a test certificate.

  The test certificate is created in memory on every run and is never added to a certificate
  store. Its public part is written next to the package (.cer). A machine trusts the package only
  after that certificate is imported into LocalMachine\TrustedPeople, which the CI install job
  does on its throwaway runner. Spike only: releases need a real signing route (spike plan §9).
  Works in Windows PowerShell 5.1 and PowerShell 7, on Windows with the Windows SDK.

.EXAMPLE
  powershell tools/package/package-windows.ps1 -Layout artifacts/package/win-x64-aot/app -Output artifacts/package/pdf-suite-win-x64.msix
#>
param(
    [Parameter(Mandatory)] [string] $Layout,
    [Parameter(Mandatory)] [string] $Output,
    [string] $Version = '0.1.0.0',
    [ValidateSet('x64', 'arm64')] [string] $Architecture = 'x64'
)

. (Join-Path $PSScriptRoot 'common.ps1')

function Get-WindowsSdkTool([string] $Name) {
    $bin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $tool = Get-ChildItem $bin -Directory -Filter '10.*' | Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName "x64\$Name" } | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $tool) { throw "$Name was not found under $bin. Install the Windows SDK." }
    $tool
}

$Output = [System.IO.Path]::GetFullPath($Output)
$staging = "$Output.staging"
if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
New-Item -ItemType Directory -Force (Split-Path $Output) | Out-Null
Copy-Item -Recurse $Layout $staging

Write-PlaceholderIcon 44 (Join-Path $staging 'Assets\Square44x44Logo.png')
Write-PlaceholderIcon 150 (Join-Path $staging 'Assets\Square150x150Logo.png')
Write-PlaceholderIcon 50 (Join-Path $staging 'Assets\StoreLogo.png')

# A full-trust desktop app. The execution alias lets the CI install job start the installed app
# with arguments, under its package identity, as a user would from a terminal.
$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
         xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
         xmlns:uap3="http://schemas.microsoft.com/appx/manifest/uap/windows10/3"
         xmlns:desktop="http://schemas.microsoft.com/appx/manifest/desktop/windows10"
         xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
         IgnorableNamespaces="uap uap3 desktop rescap">
  <Identity Name="PdfSuite.Spike" Publisher="$($PackageInfo.Publisher)" Version="$Version" ProcessorArchitecture="$Architecture" />
  <Properties>
    <DisplayName>$($PackageInfo.DisplayName)</DisplayName>
    <PublisherDisplayName>PDF Suite spike</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.26100.0" />
  </Dependencies>
  <Resources>
    <Resource Language="en-us" />
  </Resources>
  <Applications>
    <Application Id="App" Executable="PdfSuite.App.exe" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements DisplayName="$($PackageInfo.DisplayName)" Description="$($PackageInfo.Description)"
                          BackgroundColor="transparent" Square150x150Logo="Assets\Square150x150Logo.png" Square44x44Logo="Assets\Square44x44Logo.png" />
      <Extensions>
        <uap3:Extension Category="windows.appExecutionAlias" Executable="PdfSuite.App.exe" EntryPoint="Windows.FullTrustApplication">
          <uap3:AppExecutionAlias>
            <desktop:ExecutionAlias Alias="$($PackageInfo.Command).exe" />
          </uap3:AppExecutionAlias>
        </uap3:Extension>
      </Extensions>
    </Application>
  </Applications>
  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
  </Capabilities>
</Package>
"@
[System.IO.File]::WriteAllText((Join-Path $staging 'AppxManifest.xml'), $manifest)

if (Test-Path $Output) { Remove-Item $Output }
Invoke-Native (Get-WindowsSdkTool 'makeappx.exe') @('pack', '/o', '/d', $staging, '/p', $Output)

# The test certificate: RSA 3072, code signing only, valid for 30 days.
$rsa = [System.Security.Cryptography.RSA]::Create(3072)
$request = New-Object System.Security.Cryptography.X509Certificates.CertificateRequest(
    $PackageInfo.Publisher, $rsa, [System.Security.Cryptography.HashAlgorithmName]::SHA256,
    [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
$request.CertificateExtensions.Add((New-Object System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension($false, $false, 0, $true)))
$request.CertificateExtensions.Add((New-Object System.Security.Cryptography.X509Certificates.X509KeyUsageExtension(
    [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature, $true)))
$codeSigning = New-Object System.Security.Cryptography.OidCollection
[void]$codeSigning.Add((New-Object System.Security.Cryptography.Oid('1.3.6.1.5.5.7.3.3')))
$request.CertificateExtensions.Add((New-Object System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension($codeSigning, $false)))
$certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-1), [DateTimeOffset]::UtcNow.AddDays(30))

$password = [guid]::NewGuid().ToString('N')
$pfx = Join-Path $staging 'test-signing.pfx'
[System.IO.File]::WriteAllBytes($pfx, $certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $password))
try {
    Invoke-Native (Get-WindowsSdkTool 'signtool.exe') @('sign', '/q', '/fd', 'SHA256', '/f', $pfx, '/p', $password, $Output)
}
finally {
    Remove-Item $pfx
}

$cer = [System.IO.Path]::ChangeExtension($Output, '.cer')
[System.IO.File]::WriteAllBytes($cer, $certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
Remove-Item -Recurse -Force $staging

Write-Notice 'MSIX' ("{0}: {1}, signed with a test certificate ({2})" -f (Split-Path $Output -Leaf), (Format-Megabytes (Get-Item $Output).Length), $certificate.Thumbprint)
