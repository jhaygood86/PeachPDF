<#
.SYNOPSIS
Starts the example app, moves the real mouse over the element whose style asks for an image pointer, and reads which pointer the system is showing.

.DESCRIPTION
Needs a desktop session. The example's .badge element asks for a 32 x 32 pointer with its hotspot at 8,8. The script moves the mouse onto the element
and reads the current cursor with GetCursorInfo / GetIconInfo: it must have that hotspot, and must differ from the pointer over plain text. Exits 0
when the image pointer is showing, 1 otherwise.

.PARAMETER Exe
The built example, e.g. bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\PeachBrackets.WinUI.Example.exe
#>
param([Parameter(Mandatory)][string]$Exe)

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct CURSORINFO { public int Size, Flags; public IntPtr Cursor; public POINT Pos; }
    [StructLayout(LayoutKind.Sequential)] public struct ICONINFO { public bool IsIcon; public int HotX, HotY; public IntPtr Mask, Color; }
    [StructLayout(LayoutKind.Sequential)] public struct BITMAP { public int Type, Width, Height, WidthBytes; public ushort Planes, BitsPixel; public IntPtr Bits; }
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, uint data, IntPtr extra);
    [DllImport("user32.dll")] public static extern bool GetCursorInfo(ref CURSORINFO info);
    [DllImport("user32.dll")] public static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);
    [DllImport("gdi32.dll")] public static extern int GetObject(IntPtr handle, int size, out BITMAP bitmap);

    public static string Describe()
    {
        var info = new CURSORINFO { Size = Marshal.SizeOf(typeof(CURSORINFO)) };
        GetCursorInfo(ref info);
        ICONINFO icon;
        if (!GetIconInfo(info.Cursor, out icon)) return "unreadable";
        BITMAP bmp;
        var bitmap = icon.Color != IntPtr.Zero ? icon.Color : icon.Mask;
        GetObject(bitmap, Marshal.SizeOf(typeof(BITMAP)), out bmp);
        return string.Format("{0}x{1} hotspot {2},{3}", bmp.Width, bmp.Height, icon.HotX, icon.HotY);
    }

    public static void Wiggle(int x, int y)
    {
        // Real movement events (not just a position change) so the framework's pointer handling runs.
        SetCursorPos(x, y);
        for (int i = 0; i < 6; i++) { mouse_event(0x0001, i % 2 == 0 ? 1 : -1, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(60); }
    }
}
'@

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
    Start-Sleep -Seconds 8

    $all = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $nodes = @($all | Where-Object { $_.Current.ClassName -eq 'HtmlNode' })
    $badge = $nodes | Where-Object { $_.Current.Name -like '*red square*' } | Select-Object -First 1
    $text = $nodes | Where-Object { $_.Current.Name -like '*cannot be selected*' } | Select-Object -First 1
    if (-not $badge) { throw 'The badge element was not found.' }

    $r = $badge.Current.BoundingRectangle
    Write-Host ("badge at {0},{1} {2}x{3}" -f $r.X, $r.Y, $r.Width, $r.Height)
    [Native]::Wiggle([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
    Start-Sleep -Milliseconds 800   # the image loads on the first move
    [Native]::Wiggle([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
    Start-Sleep -Milliseconds 300
    $over = [Native]::Describe()
    Write-Host "over the badge: $over"
    if ($over -ne '32x32 hotspot 8,8') { $failures += "The pointer over the badge is '$over', not the 32x32 image pointer with hotspot 8,8." }

    if ($text) {
        $t = $text.Current.BoundingRectangle
        [Native]::Wiggle([int]($t.X + 20), [int]($t.Y + $t.Height / 2))
        Start-Sleep -Milliseconds 300
        $away = [Native]::Describe()
        Write-Host "over other text: $away"
        if ($away -eq $over) { $failures += 'The image pointer is still showing over other text.' }
    }
}
catch { $failures += $_.Exception.Message }
finally { if (-not $process.HasExited) { $process.Kill() } }

if ($failures) { $failures | ForEach-Object { Write-Host "FAIL: $_" }; exit 1 }
Write-Host 'OK: the image pointer is on screen.'
