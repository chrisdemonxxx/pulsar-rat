Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing,System.Windows.Forms -PassThru | Out-Null
Add-Type -MemberDefinition '
[DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
[DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
' -Name K -Namespace HK | Out-Null

$root = [System.Windows.Automation.AutomationElement]::RootElement
$p = Get-Process Pulsar

# Find forms
$wins = $root.FindAll([System.Windows.Automation.TreeScope]::Children, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)))
$hvnc = $null
foreach ($w in $wins) { if ($w.Current.Class -and $w.Current.Class -match 'FrmHVNC' -eq $false -and $w.Current.Name -like 'HVNC*') { $hvnc = $w; break } }
"HVNC: $($hvnc.Current.Name)"

# Toggle keyboard
$btnKbd = $hvnc.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'btnKeyboard')))
$btnMouse = $hvnc.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'btnMouse')))
($btnKbd.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
($btnMouse.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
"keyboard+mouse toggled"
Start-Sleep -Seconds 1

# Press Win+R
$LWIN = 0x5B; $R = 0x52; $KEYUP = 0x0002
[K.HK]::keybd_event($LWIN, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 50
[K.HK]::keybd_event($R, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 50
[K.HK]::keybd_event($R, 0, $KEYUP, [UIntPtr]::Zero)
[K.HK]::keybd_event($LWIN, 0, $KEYUP, [UIntPtr]::Zero)
Start-Sleep -Seconds 2

# Type powershell
[System.Windows.Forms.SendKeys]::SendWait("powershell{ENTER}")
Start-Sleep -Seconds 6

# Tesla command inside hidden desktop
[System.Windows.Forms.SendKeys]::SendWait('whoami{ENTER}')
Start-Sleep -Seconds 3
[System.Windows.Forms.SendKeys]::SendWait('Get-Process | Select-Object Name,Id -First 5{ENTER}')
Start-Sleep -Seconds 4

# Screenshot HVNC with the hidden desktop PowerShell inside
$bmp = New-Object System.Drawing.Bitmap(960,700)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$srcX = [int]$hvnc.Current.BoundingRectangle.X
$srcY = [int]$hvnc.Current.BoundingRectangle.Y
$g.CopyFromScreen($srcX,$srcY,0,0,(New-Object System.Drawing.Size(960,700)))
$path1 = "C:\Users\chris\Downloads\c2panel\pulsar\test-evidence\hvnc-powershell.png"
$bmp.Save($path1, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
"SHOT1: $path1"
$hvncTitle = $hvnc.Current.Name
"HVNC TITLE: $hvncTitle"
"FPS: extract manually from title"
