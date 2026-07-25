# Confirms the elevation indicator renders in the Settings window.
# Reports control captions only -- never clipboard contents, history entries, or typed text.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\_common.ps1"

$identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
$elevated = (New-Object System.Security.Principal.WindowsPrincipal($identity)).IsInRole(
    [System.Security.Principal.WindowsBuiltInRole]::Administrator)

Write-Host "launching context elevated = $elevated  (TypeyTypey inherits this)" -ForegroundColor Magenta
if ($elevated) {
    Write-Host "expecting the notice to be PRESENT" -ForegroundColor DarkGray
} else {
    Write-Host "expecting the notice to be ABSENT" -ForegroundColor DarkGray
}
Write-Host "note: with UAC disabled an admin account has no split token, so the non-elevated" -ForegroundColor DarkGray
Write-Host "      branch is unreachable. Enabling EnableLUA requires a reboot." -ForegroundColor DarkGray

Assert-Executable
Stop-TypeyTypey
$app = Start-TypeyTypey
Start-Process $script:Exe -ArgumentList '--settings' | Out-Null
Start-Sleep -Seconds 3

$settings = [Probe]::TopLevel([uint32]$app.Id) |
    Where-Object { [Probe]::Caption($_) -eq 'TypeyTypey Settings' } |
    Select-Object -First 1

if (-not $settings) {
    Stop-TypeyTypey
    throw "Settings window not found."
}

Write-Host ""
Write-Host "=== controls mentioning elevation ===" -ForegroundColor Cyan
$notice = $null
foreach ($child in [Probe]::Children($settings)) {
    $text = [Probe]::Caption($child)
    if ($text -match 'administrator|elevated') {
        Write-Host ("  [{0}] visible={1} '{2}'" -f [Probe]::Cls($child), [Probe]::IsWindowVisible($child), $text)
        # The checkbox is always present; the notice is the static label.
        if ($text -match 'Running as administrator') { $notice = $child }
    }
}

Write-Host ""
$present = $null -ne $notice -and [Probe]::IsWindowVisible($notice)
if ($present -eq $elevated) {
    Write-Host "PASS  elevation notice presence ($present) matches elevation state ($elevated)" -ForegroundColor Green
    $code = 0
} else {
    Write-Host "FAIL  notice present=$present but elevated=$elevated" -ForegroundColor Red
    $code = 1
}

Stop-TypeyTypey
exit $code
