<#
.SYNOPSIS
Starts the example app and reads what a screen reader would: its UI Automation tree.

.DESCRIPTION
Needs a desktop session. Checks that the HtmlView appears as a Document whose children are the page's text blocks, that a link is a Hyperlink
with the Invoke pattern, and that every node has a bounding rectangle on the screen. Exits 0 when everything checks out, 1 otherwise.

.PARAMETER Exe
The built example, e.g. bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\PeachBrackets.WinUI.Example.exe
#>
param([Parameter(Mandatory)][string]$Exe)

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$ae = [System.Windows.Automation.AutomationElement]
$process = Start-Process -FilePath $Exe -PassThru
$failures = @()
try {
    $byProcess = [System.Windows.Automation.PropertyCondition]::new($ae::ProcessIdProperty, $process.Id)
    $window = $null
    for ($i = 0; $i -lt 40 -and -not $window; $i++) {
        Start-Sleep -Milliseconds 500
        $window = $ae::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $byProcess)
    }
    if (-not $window) { throw 'The example did not open a window.' }
    Start-Sleep -Seconds 8   # the page loads and lays out

    $all = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $nodes = @($all | Where-Object { $_.Current.ClassName -eq 'HtmlNode' })
    $views = @($all | Where-Object { $_.Current.ClassName -eq 'HtmlView' })

    if ($views.Count -ne 1 -or $views[0].Current.ControlType -ne [System.Windows.Automation.ControlType]::Document) { $failures += 'The view is not exposed as one Document.' }
    foreach ($expected in 'PeachBrackets.WinUI', 'cannot be selected', 'touch-action: none') {
        if (-not ($nodes | Where-Object { $_.Current.Name -like "*$expected*" })) { $failures += "No text node containing '$expected'." }
    }
    foreach ($node in $nodes) {
        $r = $node.Current.BoundingRectangle
        if ($r.IsEmpty -or $r.Width -le 0 -or $r.Height -le 0) { $failures += "'$($node.Current.Name)' has no bounding rectangle." }
    }

    $link = $nodes | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Hyperlink } | Select-Object -First 1
    if (-not $link) { $failures += 'No Hyperlink node.' }
    else {
        $pattern = $null
        if (-not $link.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) { $failures += 'The Hyperlink has no Invoke pattern.' }
    }

    foreach ($node in $nodes) { '{0,-22} "{1}"' -f $node.Current.ControlType.ProgrammaticName, $node.Current.Name }
}
catch { $failures += $_.Exception.Message }
finally { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }

if ($failures.Count -gt 0) { $failures | ForEach-Object { "FAIL: $_" }; exit 1 }
'OK'
