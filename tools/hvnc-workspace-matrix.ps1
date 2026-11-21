param(
    [switch] $LiveActions,
    [ValidateSet("1280x720", "1920x1080", "2560x1440", "3840x2160")]
    [string[]] $Layouts = @("1280x720", "1920x1080", "2560x1440", "3840x2160"),
    [ValidateSet("100", "125", "150", "175")]
    [string[]] $DpiModes = @("100", "125", "150", "175"),
    [string] $OutputDirectory = "C:\Users\chris\Downloads\c2panel\pulsar\test-evidence\hvnc"
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms

$report = [ordered]@{
    StartedUtc = [DateTime]::UtcNow
    LiveActions = [bool] $LiveActions
    Layouts = @()
    Failures = @()
}

foreach ($layout in $Layouts) {
    foreach ($dpi in $DpiModes) {
        $case = [ordered]@{
            Layout = $layout
            Dpi = $dpi
            Status = "Pending"
            Screenshot = ""
        }

        try {
            $pulsar = Get-Process Pulsar -ErrorAction SilentlyContinue
            if (-not $pulsar) {
                throw "Pulsar server process is not running."
            }

            $root = [System.Windows.Automation.AutomationElement]::RootElement
            $processCondition = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
                $pulsar[0].Id)
            $windows = $root.FindAll(
                [System.Windows.Automation.TreeScope]::Children,
                $processCondition)

            $hvnc = $null
            foreach ($window in $windows) {
                if ($window.Current.Name -like "HVNC*") {
                    $hvnc = $window
                    break
                }
            }

            if (-not $hvnc) {
                $case.Status = "Skipped - HVNC window not open"
            } else {
                $rectangle = $hvnc.Current.BoundingRectangle
                $safeWidth = [Math]::Max([int] $rectangle.Width, 640)
                $safeHeight = [Math]::Max([int] $rectangle.Height, 480)
                $bitmap = New-Object System.Drawing.Bitmap($safeWidth, $safeHeight)
                $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
                $graphics.CopyFromScreen(
                    [int] $rectangle.X,
                    [int] $rectangle.Y,
                    0,
                    0,
                    (New-Object System.Drawing.Size($safeWidth, $safeHeight)))

                $safeName = "$layout-$dpi"
                $case.Screenshot = Join-Path $OutputDirectory "$safeName.png"
                $bitmap.Save($case.Screenshot, [System.Drawing.Imaging.ImageFormat]::Png)
                $graphics.Dispose()
                $bitmap.Dispose()

                $case.Status = "Captured"
            }
        }
        catch {
            $case.Status = "Failed"
            $report.Failures += $_.Exception.Message
        }

        $report.Layouts += [pscustomobject] $case
    }
}

$report.CompletedUtc = [DateTime]::UtcNow
$reportPath = Join-Path $OutputDirectory "hvnc-layout-report.json"
$report | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 $reportPath

if ($report.Failures.Count -gt 0) {
    throw "HVNC layout matrix failed: $($report.Failures -join '; ')"
}

Write-Output "HVNC layout matrix report: $reportPath"
