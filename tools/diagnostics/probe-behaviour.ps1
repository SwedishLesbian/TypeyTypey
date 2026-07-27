# Runtime regression suite. Verifies the defects fixed in v1.0.4 and v1.0.5 stay fixed.
# Reports window topology only -- never clipboard contents, history entries, or typed text.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\_common.ps1"

Assert-Executable
Stop-TypeyTypey
$app = Start-TypeyTypey
Write-Host "PID=$($app.Id)"

$failures = @()
function Check([string]$What, [bool]$Ok) {
    if ($Ok) { Write-Host "  PASS  $What" -ForegroundColor Green }
    else     { Write-Host "  FAIL  $What" -ForegroundColor Red; $script:failures += $What }
}

Show-UiWindows "1. cold start" $app.Id
Check "cold start opens no window (quiet tray start)" ((Get-UiWindows $app.Id).Count -eq 0)

Send-HistoryHotkey
Show-UiWindows "2. history hotkey, Settings never opened" $app.Id
$picker = Get-UiWindows $app.Id | Where-Object { $_.Caption -eq 'Clipboard History' }
Check "picker opens from hotkey with Settings never opened" ($null -ne $picker -and $picker.Visible)
Check "picker is topmost" ($null -ne $picker -and $picker.TopMost)
Close-Window $app.Id 'Clipboard History'

Write-Host ""
Write-Host ">> opening Settings via --settings (exercises the IPC relay)" -ForegroundColor Yellow
Start-Process $script:Exe -ArgumentList '--settings' | Out-Null
Start-Sleep -Seconds 3
Show-UiWindows "3. Settings" $app.Id
$settings = Get-UiWindows $app.Id | Where-Object { $_.Caption -eq 'TypeyTypey Settings' }
Check "--settings reached the running instance over IPC" ($null -ne $settings)
if ($settings) {
    $scale = $settings.Dpi / 96.0
    $logicalW = [math]::Round($settings.ClientW / $scale)
    $logicalH = [math]::Round($settings.ClientH / $scale)
    # v1.0.3 applied no scale factor, so these came back as raw device pixels.
    # Height rose to 760 in v1.0.5 with the third hotkey row. Keep in step with
    # WindowPlacement.DefaultSettingsClientSize.
    Check "Settings client is 615x760 logical (got ${logicalW}x${logicalH} at $($settings.Dpi) dpi)" `
          ([math]::Abs($logicalW - 615) -le 8 -and [math]::Abs($logicalH - 760) -le 8)
}

Close-Window $app.Id 'TypeyTypey Settings'
Show-UiWindows "4. Settings closed" $app.Id

Send-HistoryHotkey
Show-UiWindows "5. hotkey AFTER Settings opened and closed" $app.Id
$picker = Get-UiWindows $app.Id | Where-Object { $_.Caption -eq 'Clipboard History' }
# The v1.0.3 defect: closing Settings toggled ShowInTaskbar, recreating the form handle and
# silently destroying both hotkey registrations.
Check "hotkey survives Settings being opened and closed" ($null -ne $picker -and $picker.Visible)
Close-Window $app.Id 'Clipboard History'

Write-Host ""
Write-Host ">> --history over IPC" -ForegroundColor Yellow
Start-Process $script:Exe -ArgumentList '--history' | Out-Null
Start-Sleep -Seconds 3
Show-UiWindows "6. picker via --history" $app.Id
$picker = Get-UiWindows $app.Id | Where-Object { $_.Caption -eq 'Clipboard History' }
Check "--history opens the picker over IPC" ($null -ne $picker -and $picker.Visible)
Close-Window $app.Id 'Clipboard History'

Write-Host ""
Write-Host ">> --help renders in its own process (v1.0.5)" -ForegroundColor Yellow
$help = Start-Process $script:Exe -ArgumentList '--help' -PassThru
Start-Sleep -Seconds 4
Show-UiWindows "7. help window" $help.Id
$helpWindow = Get-UiWindows $help.Id | Where-Object { $_.Caption -eq 'TypeyTypey Help' }
# Help must not be relayed: it has to work whether or not an instance is already running.
Check "--help opens its own help window" ($null -ne $helpWindow -and $helpWindow.Visible)
Check "--help left the running instance untouched" ((Get-UiWindows $app.Id).Count -eq 0)
Close-Window $help.Id 'TypeyTypey Help'

Check "process survives closing every window" (-not $app.HasExited)

Stop-TypeyTypey
Write-Host ""
if ($failures.Count -eq 0) {
    Write-Host "ALL CHECKS PASSED" -ForegroundColor Green
    exit 0
}
Write-Host "FAILED: $($failures.Count)" -ForegroundColor Red
$failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
exit 1
