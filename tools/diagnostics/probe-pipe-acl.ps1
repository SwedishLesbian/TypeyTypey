# Prints the command pipe's security descriptor.
#
# Get-Acl and GetNamedSecurityInfoW both fail with error 87 on a named pipe path, so this opens a
# handle with READ_CONTROL and queries it as SE_KERNEL_OBJECT.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\_common.ps1"

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class PipeAcl {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern IntPtr CreateFileW(string path, uint access, uint share, IntPtr sa, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool CloseHandle(IntPtr h);
    [DllImport("advapi32.dll", SetLastError=true)]
    static extern uint GetSecurityInfo(IntPtr handle, int objectType, int securityInfo,
        out IntPtr owner, out IntPtr group, out IntPtr dacl, out IntPtr sacl, out IntPtr sd);
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern bool ConvertSecurityDescriptorToStringSecurityDescriptorW(IntPtr sd, uint revision, int securityInfo, out IntPtr str, out int len);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);

    const uint READ_CONTROL = 0x00020000;
    const uint OPEN_EXISTING = 3;
    const int  SE_KERNEL_OBJECT = 6;
    const int  INFO = 0x1 | 0x2 | 0x4;   // owner | group | dacl

    public static string GetSddl(string path, out string stage, out int err) {
        stage = "CreateFileW"; err = 0;
        IntPtr h = CreateFileW(path, READ_CONTROL, 0, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h == new IntPtr(-1)) { err = Marshal.GetLastWin32Error(); return null; }
        try {
            stage = "GetSecurityInfo";
            IntPtr o, g, d, s, sd;
            uint rc = GetSecurityInfo(h, SE_KERNEL_OBJECT, INFO, out o, out g, out d, out s, out sd);
            if (rc != 0) { err = (int)rc; return null; }
            stage = "ConvertSecurityDescriptor";
            IntPtr str; int len;
            if (!ConvertSecurityDescriptorToStringSecurityDescriptorW(sd, 1, INFO, out str, out len)) {
                err = Marshal.GetLastWin32Error(); LocalFree(sd); return null;
            }
            string result = Marshal.PtrToStringUni(str);
            LocalFree(str); LocalFree(sd);
            stage = "ok";
            return result;
        } finally { CloseHandle(h); }
    }
}
'@

Assert-Executable
Stop-TypeyTypey
$app = Start-TypeyTypey
Write-Host "PID=$($app.Id)"

$stage = ''; $err = 0
$sddl = [PipeAcl]::GetSddl("\\.\pipe\$($script:PipeName)", [ref]$stage, [ref]$err)

Write-Host ""
if (-not $sddl) {
    Stop-TypeyTypey
    throw "Failed at stage '$stage', error $err."
}

Write-Host "SDDL: $sddl" -ForegroundColor Green
Write-Host ""
$descriptor = New-Object System.Security.AccessControl.RawSecurityDescriptor($sddl)
$everyone = $false
foreach ($ace in $descriptor.DiscretionaryAcl) {
    $sid = $ace.SecurityIdentifier
    $who = try { $sid.Translate([System.Security.Principal.NTAccount]).Value } catch { $sid.Value }
    Write-Host ("  {0,-14} {1,-42} mask=0x{2:X}" -f $ace.AceType, $who, $ace.AccessMask)
    # WorldSid = Everyone, AnonymousSid = ANONYMOUS LOGON. Both appeared in the Windows default
    # that v1.0.4 replaced; neither should be present now.
    if ($sid.IsWellKnown([System.Security.Principal.WellKnownSidType]::WorldSid) -or
        $sid.IsWellKnown([System.Security.Principal.WellKnownSidType]::AnonymousSid)) { $everyone = $true }
}

Write-Host ""
if ($everyone) {
    Write-Host "FAIL  Everyone or ANONYMOUS LOGON is present on the command pipe" -ForegroundColor Red
    $code = 1
} else {
    Write-Host "PASS  pipe is restricted; no Everyone or ANONYMOUS LOGON ACE" -ForegroundColor Green
    $code = 0
}

Stop-TypeyTypey
exit $code
