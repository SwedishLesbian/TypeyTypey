# Shared probe plumbing. Dot-source from a probe: . "$PSScriptRoot\_common.ps1"
#
# Reports window topology only -- never clipboard contents, history entries, or typed text.

Set-StrictMode -Version Latest

Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class Probe {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr mon, int type, out uint x, out uint y);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

    // PER_MONITOR_AWARE_V2. Without this Windows virtualises every rect for a DPI-unaware caller,
    // returning values divided by the scale factor. See README.
    public static bool MakeDpiAware() { return SetProcessDpiAwarenessContext(new IntPtr(-4)); }

    public static List<IntPtr> TopLevel(uint pid) {
        var found = new List<IntPtr>();
        EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid) found.Add(h); return true; }, IntPtr.Zero);
        return found;
    }
    public static List<IntPtr> Children(IntPtr parent) {
        var found = new List<IntPtr>();
        EnumChildWindows(parent, (h, l) => { found.Add(h); return true; }, IntPtr.Zero);
        return found;
    }
    public static string Caption(IntPtr h) { var s = new StringBuilder(512); GetWindowText(h, s, 512); return s.ToString(); }
    public static string Cls(IntPtr h) { var s = new StringBuilder(512); GetClassName(h, s, 512); return s.ToString(); }
    public static bool TopMost(IntPtr h) { return (GetWindowLong(h, -20) & 0x00000008) != 0; }
    public static uint Dpi(IntPtr h) { uint x, y; GetDpiForMonitor(MonitorFromWindow(h, 2), 0, out x, out y); return x; }
}
'@

if (-not [Probe]::MakeDpiAware()) {
    Write-Warning "Could not set per-monitor DPI awareness. Every measurement below may be virtualised."
}

# tools/diagnostics -> repo root
$script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$script:Exe      = Join-Path $script:RepoRoot 'bin\TypeyTypey.exe'
$script:PipeName = 'TypeyTypey.Commands.4D39A83C-52EC-4620-BB73-0265522DF85E'

function Assert-Executable {
    if (-not (Test-Path $script:Exe)) {
        throw "Not found: $($script:Exe). Run .\publish.ps1 from the repository root first."
    }
    Write-Host "executable: $($script:Exe)" -ForegroundColor DarkGray
}

function Stop-TypeyTypey {
    Get-Process TypeyTypey -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 700
}

function Start-TypeyTypey {
    $p = Start-Process $script:Exe -PassThru
    Start-Sleep -Seconds 4
    return $p
}

function Get-UiWindows([int]$ProcessId) {
    $result = @()
    foreach ($h in [Probe]::TopLevel([uint32]$ProcessId)) {
        $cap = [Probe]::Caption($h)
        if ($cap -in @('TypeyTypey Settings', 'Clipboard History', 'TypeyTypey Help', 'About TypeyTypey')) {
            $r = New-Object Probe+RECT; [void][Probe]::GetWindowRect($h, [ref]$r)
            $c = New-Object Probe+RECT; [void][Probe]::GetClientRect($h, [ref]$c)
            $result += [pscustomobject]@{
                Hwnd = ('0x{0:X}' -f $h.ToInt64()); Caption = $cap
                Visible = [Probe]::IsWindowVisible($h); TopMost = [Probe]::TopMost($h)
                ClientW = ($c.R - $c.L); ClientH = ($c.B - $c.T); Dpi = [Probe]::Dpi($h)
            }
        }
    }
    return ,$result
}

function Show-UiWindows([string]$Label, [int]$ProcessId) {
    Write-Host ""
    Write-Host "=== $Label ===" -ForegroundColor Cyan
    $windows = Get-UiWindows $ProcessId
    if ($windows.Count -eq 0) { Write-Host "  (no TypeyTypey UI windows)" -ForegroundColor DarkGray; return }
    foreach ($w in $windows) {
        $scale = [math]::Round($w.Dpi / 96.0, 2)
        Write-Host ("  {0,-19} {1} visible={2,-5} topmost={3,-5} client={4}x{5} dpi={6} ({7}x) => logical {8}x{9}" -f `
            $w.Caption, $w.Hwnd, $w.Visible, $w.TopMost, $w.ClientW, $w.ClientH, $w.Dpi, $scale,
            [math]::Round($w.ClientW / $scale), [math]::Round($w.ClientH / $scale))
    }
}

function Send-HistoryHotkey {
    Write-Host ">> Ctrl+Alt+Shift+V" -ForegroundColor Yellow
    [Probe]::keybd_event(0x11,0,0,[UIntPtr]::Zero); [Probe]::keybd_event(0x12,0,0,[UIntPtr]::Zero)
    [Probe]::keybd_event(0x10,0,0,[UIntPtr]::Zero); [Probe]::keybd_event(0x56,0,0,[UIntPtr]::Zero)
    Start-Sleep -Milliseconds 90
    [Probe]::keybd_event(0x56,0,2,[UIntPtr]::Zero); [Probe]::keybd_event(0x10,0,2,[UIntPtr]::Zero)
    [Probe]::keybd_event(0x12,0,2,[UIntPtr]::Zero); [Probe]::keybd_event(0x11,0,2,[UIntPtr]::Zero)
    Start-Sleep -Seconds 2
}

function Close-Window([int]$ProcessId, [string]$Caption) {
    foreach ($h in [Probe]::TopLevel([uint32]$ProcessId)) {
        if ([Probe]::Caption($h) -eq $Caption) { [void][Probe]::PostMessageW($h, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) }
    }
    Start-Sleep -Milliseconds 1200
}
