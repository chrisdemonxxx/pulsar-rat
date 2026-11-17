param([int]$Downs = 0, [int]$SubDowns = -1, [string]$ExpectTitle = '', [int]$WaitSec = 7, [int]$Retries = 3)
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes | Out-Null
Add-Type -MemberDefinition '
[DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint f,uint dx,uint dy,uint d,UIntPtr e);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder t, int c);
' -Name KG -Namespace WG | Out-Null

function Fg-Title {
    $sb = New-Object System.Text.StringBuilder 512
    $h = [WG.KG]::GetForegroundWindow(); [WG.KG]::GetWindowText($h,$sb,512)|Out-Null; $sb.ToString()
}
function Key([byte]$vk){ [WG.KG]::keybd_event($vk,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 70; [WG.KG]::keybd_event($vk,0,2,[UIntPtr]::Zero); Start-Sleep -Milliseconds 200 }

$root=[System.Windows.Automation.AutomationElement]::RootElement
$p=Get-Process Pulsar

for($attempt=1;$attempt -le $Retries;$attempt++){
    # activate main window via ALT-tap unlock
    $main=$null
    foreach($w in $root.FindAll([System.Windows.Automation.TreeScope]::Children,(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$p.Id)))){
        if($w.Current.Name -like 'Pulsar Premium*'){ $main=$w }
    }
    if(-not $main){ Write-Output 'NO MAIN WINDOW'; exit 1 }
    $hwnd=[IntPtr]$main.Current.NativeWindowHandle
    for($try=0;$try -lt 8;$try++){
        [WG.KG]::keybd_event(0x12,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 40; [WG.KG]::keybd_event(0x12,0,2,[UIntPtr]::Zero)
        [WG.KG]::SetForegroundWindow($hwnd)|Out-Null
        Start-Sleep -Milliseconds 400
        if((Fg-Title) -like 'Pulsar Premium*'){ break }
    }
    if((Fg-Title) -notlike 'Pulsar Premium*'){ Write-Output "attempt ${attempt}: ACTIVATION FAILED"; continue }

    # select the client row (left click) -> gives elementhost kb focus
    $data=$main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::DataItem)))
    if(-not $data){ Write-Output 'NO DATAITEM'; exit 3 }
    $b=$data.Current.BoundingRectangle
    [WG.KG]::SetCursorPos([int]$b.X+[int]$b.Width/2,[int]$b.Y+[int]$b.Height/2)|Out-Null
    Start-Sleep -Milliseconds 250
    [WG.KG]::mouse_event(2,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [WG.KG]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
    Start-Sleep -Milliseconds 350
    # move cursor off the row so hover tooltips can't interfere
    [WG.KG]::SetCursorPos(10,900)|Out-Null

    # open the context strip via APPS key (keyboard focus is on the client list host)
    Key 0x5D
    Start-Sleep -Milliseconds 1100

    # navigate
    for($i=0;$i -lt $Downs;$i++){ Key 0x28 }
    if($SubDowns -ge 0){
        Key 0x27
        Start-Sleep -Milliseconds 500
        for($i=0;$i -lt $SubDowns;$i++){ Key 0x28 }
        Key 0x0D
    } else { Key 0x0D }

    Start-Sleep -Seconds $WaitSec
    foreach($w in $root.FindAll([System.Windows.Automation.TreeScope]::Children,(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$p.Id)))){
        if($ExpectTitle -eq '__all__'){ Write-Output "WIN: $($w.Current.Name)" }
        elseif($w.Current.Name -like "*$ExpectTitle*"){ Write-Output "WINDOW OK (try $attempt): $($w.Current.Name)"; exit 0 }
    }
    Write-Output "attempt ${attempt}: no '$ExpectTitle' window"
}
Write-Output "FAILED after $Retries attempts"
