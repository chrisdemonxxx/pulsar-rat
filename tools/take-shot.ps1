param(
    [Parameter(Mandatory=$true)][string]$WindowTitle,
    [Parameter(Mandatory=$true)][string]$OutFile
)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -AssemblyName System.Drawing
$sig = @'
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
'@
$native = Add-Type -MemberDefinition $sig -Name NativeWin -Namespace Win32 -PassThru
$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $WindowTitle)
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "NOTFOUND: $WindowTitle"; exit 1 }
$hwnd = [IntPtr]$win.Current.NativeWindowHandle
if ($hwnd -ne [IntPtr]::Zero) {
    $native::ShowWindow($hwnd, 9) | Out-Null
    $native::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 600
}
$r = $win.Current.BoundingRectangle
if ($r.Width -le 0 -or $r.Height -le 0) { Write-Output "EMPTYRECT: $WindowTitle"; exit 1 }
$bmp = New-Object System.Drawing.Bitmap([int]$r.Width, [int]$r.Height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, (New-Object System.Drawing.Size([int]$r.Width, [int]$r.Height)))
$bmp.Save($OutFile, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Output "SAVED: $OutFile ($([int]$r.Width)x$([int]$r.Height))"
