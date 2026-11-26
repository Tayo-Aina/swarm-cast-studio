param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Out,
    [string]$Text = "glm",
    [string]$AutomationId = "ModelSearchBox",
    [int]$WaitSeconds = 7
)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class DpiAwareTyped {
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("shcore.dll")] public static extern int SetProcessDpiAwareness(int value);
}
"@
[DpiAwareTyped]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
[DpiAwareTyped]::SetProcessDpiAwareness(2) | Out-Null

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win32Typed {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
}
"@

$process = Start-Process -FilePath $Exe -PassThru
Start-Sleep -Seconds $WaitSeconds
$process.Refresh()
if ($process.HasExited) { throw "App exited (code $($process.ExitCode))." }

$handle = $process.MainWindowHandle
if ($handle -eq [IntPtr]::Zero) { throw "No main window handle." }
[Win32Typed]::ShowWindow($handle, 9) | Out-Null
[Win32Typed]::BringWindowToTop($handle) | Out-Null
[Win32Typed]::SetForegroundWindow($handle) | Out-Null
Start-Sleep -Milliseconds 600

$root = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
$editCondition = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Edit)
$edits = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $editCondition)

$candidates = @()
for ($i = 0; $i -lt $edits.Count; $i++) {
    $element = $edits.Item($i)
    $rect = $element.Current.BoundingRectangle
    $candidates += [pscustomobject]@{
        Index = $i
        Name = $element.Current.Name
        AutomationId = $element.Current.AutomationId
        X = [int]$rect.X
        Y = [int]$rect.Y
        Width = [int]$rect.Width
        Element = $element
    }
}

$candidates | Select-Object Index, Name, AutomationId, X, Y, Width | Format-Table -AutoSize | Out-String | Write-Output

if ($candidates.Count -eq 0) { throw "No Edit controls found." }

$target = $null
if ($AutomationId) {
    $byId = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $AutomationId)
    $element = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $byId)
    if ($element) {
        $target = [pscustomobject]@{ Index = -1; Name = $element.Current.Name; AutomationId = $AutomationId; Element = $element }
    }
}
if (-not $target) {
    $target = $candidates | Sort-Object Y -Descending | Select-Object -First 1
}
Write-Output "typing into: automationId=$AutomationId name='$($target.Name)'"

$target.Element.SetFocus()
Start-Sleep -Milliseconds 300
$valuePattern = $target.Element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
$valuePattern.SetValue($Text)
Start-Sleep -Milliseconds 900

$rect = New-Object Win32Typed+RECT
[Win32Typed]::GetWindowRect($handle, [ref]$rect) | Out-Null
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top

$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$hdc = $graphics.GetHdc()
$printed = [Win32Typed]::PrintWindow($handle, $hdc, 2)
$graphics.ReleaseHdc($hdc)
$graphics.Dispose()

if (-not $printed) {
    $bitmap.Dispose()
    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size($width, $height)))
    $graphics.Dispose()
}

$directory = Split-Path -Parent $Out
if ($directory -and -not (Test-Path $directory)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}
$bitmap.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()

if (-not $process.HasExited) { $process.Kill() }

Write-Output "typed='$Text' png=$Out"
