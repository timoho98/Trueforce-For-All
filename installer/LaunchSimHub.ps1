# Launch SimHub and force its main window to the foreground regardless of
# the user's "Start minimized" preference. Invoked by the installer's
# Finished-page "Launch SimHub now" action so users immediately see the
# app instead of a taskbar button.
#
# Why this script exists, in three steps:
#   1. The plain Start-Process / cmd /c start path launches SimHub but
#      SimHub then applies its own StartMinimized=True user setting and
#      self-minimizes during WPF startup.
#   2. A naive ShowWindow(SW_RESTORE) + SetForegroundWindow from this
#      script doesn't help, because Windows refuses foreground-steal
#      calls from a hidden background process unless the calling thread
#      meets the foreground-allowed criteria. ShowWindow itself may also
#      no-op when issued cross-process under those conditions.
#   3. The AttachThreadInput dance below is the documented Microsoft
#      workaround: attach our input queue to both the current foreground
#      window's thread and SimHub's thread, which makes us a peer of the
#      foreground app for the duration of the calls. ShowWindow,
#      SetWindowPos, and SetForegroundWindow then actually take effect.
#
# Logs to %TEMP%\TfaLaunchSimHub.log so failed installs can be debugged
# without having to instrument the installer.
#
# Args:
#   -SimHubExe <path>   Absolute path to SimHubWPF.exe, set by the
#                       installer at install time so this script doesn't
#                       have to guess the install location.

param(
    [Parameter(Mandatory = $true)][string]$SimHubExe
)

$ErrorActionPreference = 'Continue'
$logPath = Join-Path $env:TEMP 'TfaLaunchSimHub.log'

function Write-Log([string]$msg) {
    try {
        $stamp = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss.fff')
        Add-Content -Path $logPath -Value "[$stamp] $msg" -ErrorAction SilentlyContinue
    } catch {}
}

Write-Log "Begin. SimHubExe=$SimHubExe"

if (-not (Test-Path $SimHubExe)) {
    Write-Log "SimHubExe not found; exiting."
    exit 0
}

$workingDir = Split-Path -Parent $SimHubExe

try {
    Start-Process -FilePath $SimHubExe -WorkingDirectory $workingDir -WindowStyle Normal | Out-Null
    Write-Log "Start-Process issued for SimHubWPF."
} catch {
    Write-Log "Start-Process threw: $($_.Exception.Message)"
    exit 0
}

# Inline P/Invoke shim. Compiled once via Add-Type. Covers everything we
# need for the foreground dance plus a synthetic SC_RESTORE fallback for
# the case where ShowWindow is silently swallowed.
Add-Type -ErrorAction SilentlyContinue @'
using System;
using System.Runtime.InteropServices;
public class TfaLaunchInterop {
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", SetLastError=true)] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll", CharSet=CharSet.Auto)] public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    public const int  SW_HIDE         = 0;
    public const int  SW_SHOWNORMAL   = 1;
    public const int  SW_SHOW         = 5;
    public const int  SW_RESTORE      = 9;
    public const uint WM_SYSCOMMAND   = 0x0112;
    public const int  SC_RESTORE      = 0xF120;
    public static readonly IntPtr HWND_TOPMOST    = new IntPtr(-1);
    public static readonly IntPtr HWND_NOTOPMOST  = new IntPtr(-2);
    public const uint SWP_NOMOVE      = 0x0002;
    public const uint SWP_NOSIZE      = 0x0001;
    public const uint SWP_NOACTIVATE  = 0x0010;
    public const uint SWP_SHOWWINDOW  = 0x0040;
}
'@

# Poll for SimHub's window. SimHub's WPF startup can be slow on cold
# machines; 20s gives headroom for HDDs and first-icon-cache delays.
# We accept the first non-zero MainWindowHandle from any SimHubWPF
# process; if the user already has SimHub running we'll just restore
# that one instead of waiting for a second instance that won't appear.
$timeoutSeconds = 20
$start = Get-Date
$target = $null
do {
    Start-Sleep -Milliseconds 250
    $target = Get-Process -Name 'SimHubWPF' -ErrorAction SilentlyContinue |
              Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } |
              Select-Object -First 1
} until ($target -or ((Get-Date) - $start).TotalSeconds -ge $timeoutSeconds)

if ($null -eq $target) {
    Write-Log "Timed out waiting for SimHubWPF MainWindowHandle. Exiting."
    exit 0
}

$handle = $target.MainWindowHandle
Write-Log "Found SimHubWPF pid=$($target.Id) hWnd=$handle iconic=$([TfaLaunchInterop]::IsIconic($handle))"

# AttachThreadInput dance: glue our (PowerShell) thread, the current
# foreground window's thread, and SimHub's thread together so the
# ShowWindow / SetForegroundWindow calls below are treated as
# legitimate from-foreground actions instead of background-steal
# attempts.
$ourTid = [TfaLaunchInterop]::GetCurrentThreadId()
$fgHwnd = [TfaLaunchInterop]::GetForegroundWindow()
$fgPid  = 0
$fgTid  = [TfaLaunchInterop]::GetWindowThreadProcessId($fgHwnd, [ref]$fgPid)
$tgtPid = 0
$tgtTid = [TfaLaunchInterop]::GetWindowThreadProcessId($handle, [ref]$tgtPid)
Write-Log "Threads: our=$ourTid fg=$fgTid (pid $fgPid) target=$tgtTid (pid $tgtPid)"

$attachedFg = $false
$attachedTgt = $false
try {
    if ($fgTid -ne 0 -and $fgTid -ne $ourTid) {
        $attachedFg = [TfaLaunchInterop]::AttachThreadInput($ourTid, $fgTid, $true)
    }
    if ($tgtTid -ne 0 -and $tgtTid -ne $ourTid) {
        $attachedTgt = [TfaLaunchInterop]::AttachThreadInput($ourTid, $tgtTid, $true)
    }
    Write-Log "Attach results: fg=$attachedFg target=$attachedTgt"

    # Belt-and-suspenders: restore via multiple paths. SC_RESTORE is the
    # synthetic command path that target processes always honor; ShowWindow
    # is direct; SetWindowPos with TOPMOST/NOTOPMOST nudges Z-order.
    if ([TfaLaunchInterop]::IsIconic($handle)) {
        [TfaLaunchInterop]::SendMessage($handle, [TfaLaunchInterop]::WM_SYSCOMMAND, [IntPtr][TfaLaunchInterop]::SC_RESTORE, [IntPtr]::Zero) | Out-Null
    }
    [TfaLaunchInterop]::ShowWindow($handle, [TfaLaunchInterop]::SW_SHOW) | Out-Null
    [TfaLaunchInterop]::ShowWindow($handle, [TfaLaunchInterop]::SW_RESTORE) | Out-Null
    [TfaLaunchInterop]::ShowWindowAsync($handle, [TfaLaunchInterop]::SW_RESTORE) | Out-Null
    [TfaLaunchInterop]::SetWindowPos($handle, [TfaLaunchInterop]::HWND_TOPMOST,   0, 0, 0, 0, ([TfaLaunchInterop]::SWP_NOMOVE -bor [TfaLaunchInterop]::SWP_NOSIZE -bor [TfaLaunchInterop]::SWP_SHOWWINDOW)) | Out-Null
    [TfaLaunchInterop]::SetWindowPos($handle, [TfaLaunchInterop]::HWND_NOTOPMOST, 0, 0, 0, 0, ([TfaLaunchInterop]::SWP_NOMOVE -bor [TfaLaunchInterop]::SWP_NOSIZE -bor [TfaLaunchInterop]::SWP_SHOWWINDOW)) | Out-Null
    [TfaLaunchInterop]::BringWindowToTop($handle) | Out-Null
    [TfaLaunchInterop]::SetForegroundWindow($handle) | Out-Null
    Write-Log "Restore + foreground calls issued."
} catch {
    Write-Log "Restore block threw: $($_.Exception.Message)"
} finally {
    if ($attachedFg)  { [TfaLaunchInterop]::AttachThreadInput($ourTid, $fgTid,  $false) | Out-Null }
    if ($attachedTgt) { [TfaLaunchInterop]::AttachThreadInput($ourTid, $tgtTid, $false) | Out-Null }
}

# Some users report SimHub re-minimizing itself a beat after our restore
# (its WPF startup applies the StartMinimized setting late). Wait a
# second and check; if it's iconic again, repeat the restore.
Start-Sleep -Milliseconds 800
if ([TfaLaunchInterop]::IsIconic($handle)) {
    Write-Log "Window re-minimized after restore; retrying."
    [TfaLaunchInterop]::SendMessage($handle, [TfaLaunchInterop]::WM_SYSCOMMAND, [IntPtr][TfaLaunchInterop]::SC_RESTORE, [IntPtr]::Zero) | Out-Null
    [TfaLaunchInterop]::ShowWindow($handle, [TfaLaunchInterop]::SW_RESTORE) | Out-Null
    [TfaLaunchInterop]::ShowWindowAsync($handle, [TfaLaunchInterop]::SW_RESTORE) | Out-Null
    [TfaLaunchInterop]::BringWindowToTop($handle) | Out-Null
    [TfaLaunchInterop]::SetForegroundWindow($handle) | Out-Null
}

Write-Log "Done."
exit 0
