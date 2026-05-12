# Launch SimHub and force the main window to the foreground regardless of
# the user's "Start minimized" preference. Invoked by the installer's
# Finished-page "Launch SimHub now" action so users immediately see the
# app instead of a taskbar button (or nothing at all).
#
# Why this script exists: even with "cmd /c start" or ShellExecute the
# launched SimHubWPF.exe process honors its own user.config flags. Users
# with StartMinimized=True (common; SimHub ships it as a popular tweak)
# would otherwise see only a taskbar button after our installer says
# "Launch SimHub now". We poll for the window handle to appear, then
# call ShowWindow(SW_RESTORE) + SetForegroundWindow to overrule the
# self-minimize that SimHub does during its own startup.
#
# Args:
#   -SimHubExe <path>   Absolute path to SimHubWPF.exe (set by the installer
#                       at install time so this script doesn't have to guess
#                       the install location).

param(
    [Parameter(Mandatory = $true)][string]$SimHubExe
)

$ErrorActionPreference = 'SilentlyContinue'

if (-not (Test-Path $SimHubExe)) {
    # No SimHubWPF.exe at the path the installer expected: nothing to do.
    # Don't surface an error since this is a best-effort post-install nicety.
    exit 0
}

$workingDir = Split-Path -Parent $SimHubExe

# Detach: Start-Process returns immediately; the new process is independent
# of this PowerShell instance and survives our exit.
Start-Process -FilePath $SimHubExe -WorkingDirectory $workingDir -WindowStyle Normal | Out-Null

# Compile a small Win32 shim for ShowWindow / SetForegroundWindow / IsIconic.
# Done inline so the script is self-contained (no extra files to ship).
Add-Type -ErrorAction SilentlyContinue @'
using System;
using System.Runtime.InteropServices;
public class TfaLaunchInterop {
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    public const int SW_RESTORE = 9;
    public const int SW_SHOWNORMAL = 1;
}
'@

# Poll until SimHubWPF has produced a top-level window handle, or we time
# out. SimHub's WPF startup can take a few seconds on cold machines, and on
# even slower machines (HDD, mechanical, first-run icon caching) up to 15s
# is realistic. 250ms granularity keeps the loop cheap.
$timeoutSeconds = 15
$start = Get-Date
$target = $null
do {
    Start-Sleep -Milliseconds 250
    $target = Get-Process -Name 'SimHubWPF' -ErrorAction SilentlyContinue |
              Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } |
              Select-Object -First 1
} until ($target -or ((Get-Date) - $start).TotalSeconds -ge $timeoutSeconds)

if ($null -ne $target) {
    $handle = $target.MainWindowHandle
    if ([TfaLaunchInterop]::IsIconic($handle)) {
        [TfaLaunchInterop]::ShowWindow($handle, [TfaLaunchInterop]::SW_RESTORE) | Out-Null
    } else {
        # Not iconic but possibly off-screen / hidden behind other windows.
        # SW_SHOWNORMAL is a no-op on a normally-displayed window so this is
        # safe to call unconditionally.
        [TfaLaunchInterop]::ShowWindow($handle, [TfaLaunchInterop]::SW_SHOWNORMAL) | Out-Null
    }
    [TfaLaunchInterop]::SetForegroundWindow($handle) | Out-Null
}

exit 0
