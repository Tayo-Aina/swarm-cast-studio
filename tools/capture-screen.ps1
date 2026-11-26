param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Out,
    [string]$Text = "",
    [int]$WaitSeconds = 7
)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class ScreenCaptureNative {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
}
"@
[ScreenCaptureNative]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

$process = Start-Process -FilePath $Exe -PassThru
Start-Sleep -Seconds $WaitSeconds
$process.Refresh()
if ($process.HasExited) { throw "App exited (code $($process.ExitCode))." }

$handle = $process.MainWindowHandle
if ($handle -eq [IntPtr]::Zero) { throw "No main window handle." }

[ScreenCaptureNative]::ShowWindow($handle, 9) | Out-Null
[ScreenCaptureNative]::BringWindowToTop($handle) | Out-Null

$foreground = [ScreenCaptureNative]::GetForegroundWindow()
$foregroundThread = [ScreenCaptureNative]::GetWindowThreadProcessId($foreground, [IntPtr]::Zero)
$currentThread = [ScreenCaptureNative]::GetCurrentThreadId()
[ScreenCaptureNative]::AttachThreadInput($currentThread, $foregroundThread, $true) | Out-Null
[ScreenCaptureNative]::SetForegroundWindow($handle) | Out-Null
[ScreenCaptureNative]::AttachThreadInput($currentThread, $foregroundThread, $false) | Out-Null
Start-Sleep -Milliseconds 900

if ($Text) {
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        "ModelSearchBox")
    $box = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($box) {
        $box.SetFocus()
        $valuePattern = $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
        $valuePattern.SetValue($Text)
        Start-Sleep -Milliseconds 900
    }
}

$rect = New-Object ScreenCaptureNative+RECT
[ScreenCaptureNative]::GetWindowRect($handle, [ref]$rect) | Out-Null
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -le 0 -or $height -le 0) { throw "Bad window rect." }

$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size($width, $height)))
$graphics.Dispose()

$directory = Split-Path -Parent $Out
if ($directory -and -not (Test-Path $directory)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}
$bitmap.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()

if (-not $process.HasExited) { $process.Kill() }
Write-Output "size=${width}x${height} png=$Out"
