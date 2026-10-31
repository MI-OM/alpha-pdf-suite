<#
.SYNOPSIS
  Spike S3: drives the app's Print dialog on Windows through to the start of a print job.

.DESCRIPTION
  Starts the app on a PDF, presses Ctrl+P, makes sure "Microsoft Print to PDF" is the selected
  printer, and presses Print. The app passes the printer and its settings to the worker, which
  starts the job; the Print to PDF driver then asks where to save. That Save dialog appearing
  shows the printer name and settings from the dialog opened the printer and started a job. The
  script then cancels the Save dialog and checks that the app reports the cancellation rather
  than an error. (The printed output itself is checked by the worker print test.)

  Safety: Print is pressed only after the dialog shows "Microsoft Print to PDF" as the selected
  printer; otherwise the dialog is cancelled, so nothing reaches a real printer.

  On Windows 11 the app's classic PrintDlgW call is answered by the modern "Windows Print" dialog,
  hosted by a system process and titled "<app> - Print"; the classic dialog is titled "Print".
  Both are handled. The driver's Save dialog is parented to the app's window.

  Requires Windows PowerShell 5.1 (it uses the .NET Framework UI Automation client).
#>
param(
    [Parameter(Mandatory)] [string] $App,
    [Parameter(Mandatory)] [string] $Pdf
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms
Add-Type -TypeDefinition @'
public static class Native
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(System.IntPtr hWnd);
}
'@

$A = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$ControlType = [System.Windows.Automation.ControlType]
$printer = 'Microsoft Print to PDF'

function WaitFor([scriptblock] $probe, [int] $seconds = 15) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do { $result = & $probe; if ($result) { return $result }; Start-Sleep -Milliseconds 250 } while ((Get-Date) -lt $deadline)
    return $null
}

function Condition($property, $value) { New-Object System.Windows.Automation.PropertyCondition($property, $value) }

$process = Start-Process -FilePath (Resolve-Path $App) -ArgumentList "`"$((Resolve-Path $Pdf).Path)`"" -PassThru
try {
    WaitFor { $process.Refresh(); $process.MainWindowHandle -ne 0 } | Out-Null
    Start-Sleep -Seconds 2
    $appWindow = $A::FromHandle($process.MainWindowHandle)
    [void][Native]::SetForegroundWindow($process.MainWindowHandle)
    [System.Windows.Forms.SendKeys]::SendWait('^p')

    $dialog = WaitFor {
        $A::RootElement.FindAll($Scope::Children, [System.Windows.Automation.Condition]::TrueCondition) |
            Where-Object { $_.Current.Name -eq 'Print' -or $_.Current.Name -like '*- Print' } | Select-Object -First 1
    }
    if (-not $dialog) { throw 'The Print dialog did not appear.' }
    $modern = $dialog.Current.Name -ne 'Print'
    "Print dialog shown: '$($dialog.Current.Name)' ($(if ($modern) { 'modern Windows Print' } else { 'classic' }))."
    Start-Sleep -Seconds 2

    $combo = $dialog.FindFirst($Scope::Descendants, $(if ($modern) { Condition $A::AutomationIdProperty 'printerSelector' } else { Condition $A::ControlTypeProperty $ControlType::ComboBox }))
    function SelectedPrinter { ($combo.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection() | ForEach-Object { $_.Current.Name }) -join ', ' }
    if ((SelectedPrinter) -ne $printer) {
        $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        Start-Sleep -Milliseconds 500
        $item = $A::RootElement.FindFirst($Scope::Descendants, (New-Object System.Windows.Automation.AndCondition(
            (Condition $A::ControlTypeProperty $ControlType::ListItem), (Condition $A::NameProperty $printer))))
        if ($item) { $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() }
        Start-Sleep -Seconds 2
    }

    $selected = SelectedPrinter
    if ($selected -ne $printer) {
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        throw "Selected printer is '$selected', not '$printer'; the dialog was cancelled and nothing was printed."
    }
    "Selected printer: $selected"
    $dialog.FindFirst($Scope::Descendants, $(if ($modern) { Condition $A::AutomationIdProperty 'PrintButton' } else { Condition $A::AutomationIdProperty '1' })).
        GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

    $saveCondition = New-Object System.Windows.Automation.AndCondition(
        (Condition $A::ControlTypeProperty $ControlType::Window), (Condition $A::NameProperty 'Save Print Output As'))
    $save = WaitFor { $appWindow.FindFirst($Scope::Descendants, $saveCondition) } 20
    if (-not $save) { throw 'The driver''s Save dialog did not appear, so the job did not start.' }
    "Print job started: the driver's Save dialog appeared (process $($save.Current.ProcessId); app $($process.Id))."

    # Closing the dialog is the same as pressing Cancel.
    $save.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    $statusCondition = Condition $A::ControlTypeProperty $ControlType::Text
    $status = WaitFor {
        $text = $appWindow.FindFirst($Scope::Descendants, $statusCondition).Current.Name
        if ($text -like '*cancelled*' -or $text -like '*couldn''t print*' -or $text -like 'Sent *') { $text }
    } 20
    "App status after cancelling the Save dialog: '$status'"
    if ($status -ne 'Printing was cancelled.') { throw 'Expected the app to report the cancellation.' }
    'PASS'
}
finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id }
}
