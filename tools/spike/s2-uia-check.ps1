<#
.SYNOPSIS
  Spike S2: checks what the app exposes to Windows UI Automation, which is what NVDA and Narrator read.

.DESCRIPTION
  Starts the app on a PDF, prints the accessibility tree of the document view, then presses keys
  and records which element has focus after each one and how many UIA focus-changed events fired.
  Screen readers announce those events, so this shows what a screen-reader user would hear,
  without judging how well a real screen reader presents it. That part needs a person (see the
  checklist in docs/research/spike-results.md, S2).

  Requires Windows PowerShell 5.1 (it uses the .NET Framework UI Automation client).

.EXAMPLE
  powershell -File tools\spike\s2-uia-check.ps1 -App src\PdfSuite.App\bin\Debug\net10.0\PdfSuite.App.exe -Pdf fixtures\generated\image-only-middle-page.pdf
#>
param(
    [Parameter(Mandatory)] [string] $App,
    [Parameter(Mandatory)] [string] $Pdf
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms
Add-Type -ReferencedAssemblies UIAutomationClient, UIAutomationTypes, WindowsBase -TypeDefinition @'
using System.Collections.Generic;
using System.Windows.Automation;
public static class FocusLog
{
    public static readonly List<string> Events = new List<string>();
    public static void Start()
    {
        Automation.AddAutomationFocusChangedEventHandler((sender, e) =>
        {
            var element = sender as AutomationElement;
            if (element == null) return;
            try { lock (Events) Events.Add(element.Current.ControlType.ProgrammaticName + " | " + element.Current.Name); }
            catch (ElementNotAvailableException) { }
        });
    }
}
public static class Native
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(System.IntPtr hWnd);
}
'@

$A = [System.Windows.Automation.AutomationElement]
$TypeProp = $A::ControlTypeProperty
$ControlType = [System.Windows.Automation.ControlType]
$Scope = [System.Windows.Automation.TreeScope]

function Describe($element) {
    if ($null -eq $element) { return '(none)' }
    $name = $element.Current.Name
    if ($name.Length -gt 60) { $name = $name.Substring(0, 60) + '...' }
    "{0} | {1}" -f $element.Current.ControlType.ProgrammaticName.Replace('ControlType.', ''), $name
}

$process = Start-Process -FilePath (Resolve-Path $App) -ArgumentList "`"$((Resolve-Path $Pdf).Path)`"" -PassThru
try {
    $deadline = (Get-Date).AddSeconds(30)
    while ($process.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200; $process.Refresh() }
    $window = $A::FromHandle($process.MainWindowHandle)

    # Wait for the document view and its first page element (text loads from the worker).
    $documentCondition = New-Object System.Windows.Automation.PropertyCondition($TypeProp, $ControlType::Document)
    $groupCondition = New-Object System.Windows.Automation.PropertyCondition($TypeProp, $ControlType::Group)
    $textCondition = New-Object System.Windows.Automation.PropertyCondition($TypeProp, $ControlType::Text)
    do {
        Start-Sleep -Milliseconds 250
        $document = $window.FindFirst($Scope::Descendants, $documentCondition)
        $groups = if ($document) { $document.FindAll($Scope::Children, $groupCondition) } else { @() }
    } while ($groups.Count -eq 0 -and (Get-Date) -lt $deadline)

    "=== Accessibility tree of the document view"
    "Document: $(Describe $document)"
    foreach ($group in $groups) {
        $lines = $group.FindAll($Scope::Children, $textCondition)
        "  Group: {0}  ({1} text lines, keyboard-focusable: {2})" -f $group.Current.Name, $lines.Count, $group.Current.IsKeyboardFocusable
        $lines | Select-Object -First 2 | ForEach-Object { "      $(Describe $_)" }
    }

    "=== Keyboard reading: focused element after each key"
    [FocusLog]::Start()
    [void][Native]::SetForegroundWindow($process.MainWindowHandle)
    $document.SetFocus()
    Start-Sleep -Milliseconds 500
    $keys = @(
        @('Down', '{DOWN}'), @('Down', '{DOWN}'), @('Down', '{DOWN}'),
        @('PageDown', '{PGDN}'), @('Down', '{DOWN}'), @('Up', '{UP}'), @('Up', '{UP}'),
        @('Ctrl+End', '^{END}'), @('Ctrl+Home', '^{HOME}')
    )
    foreach ($key in $keys) {
        $before = [FocusLog]::Events.Count
        [System.Windows.Forms.SendKeys]::SendWait($key[1])
        Start-Sleep -Milliseconds 700
        $events = [FocusLog]::Events.Count - $before
        "{0,-10} -> {1}   ({2} focus event(s))" -f $key[0], (Describe $A::FocusedElement), $events
    }
}
finally {
    [System.Windows.Automation.Automation]::RemoveAllEventHandlers()
    if (-not $process.HasExited) { Stop-Process -Id $process.Id }
}
