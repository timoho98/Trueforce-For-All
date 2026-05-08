# Adds Trueforce For All to SimHub's PluginsActivation.json so the plugin
# is enabled and surfaced in the main-menu sidebar on first launch after
# a fresh install. Idempotent: if an entry for our class already exists
# (any prior install, manual enable via Add/remove feature, etc.), the
# script exits without changes so a user who deliberately disabled or
# hid the plugin doesn't get their choice overridden by an upgrade.
#
# Backs up the existing JSON to <path>.bak before modifying. Writes
# UTF-8 without BOM to match SimHub's own writer; using Set-Content
# -Encoding UTF8 in Windows PowerShell 5.1 emits a BOM, so we use
# System.IO.File with a UTF8Encoding($false) instance.
#
# Invoked from TrueforceForAll.iss [Run] section; runs hidden under the
# elevated installer process. Exit 0 on success or no-op, non-zero on
# unexpected failure (caller doesn't currently abort the install on
# this failing — manual Add/remove feature still works as a fallback).

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ConfigPath
)

$ErrorActionPreference = "Stop"
$class = "TrueforceForAll.Plugin.TrueforcePlugin"

function Save-Json([object]$obj, [string]$path) {
    $json = ($obj | ConvertTo-Json -Depth 8)
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($path, $json, $utf8NoBom)
}

try {
    if (-not (Test-Path $ConfigPath)) {
        # PluginsActivation.json doesn't exist (extremely fresh SimHub).
        # Create it with just our entry. SimHub will merge in its own
        # defaults on first launch.
        $arr = @([PSCustomObject]@{
            ClassName              = $class
            IsEnabled              = $true
            ShowInMainMenu         = $true
            ShowInMainMenuPosition = 0
        })
        Save-Json $arr $ConfigPath
        Write-Host "[TrueforceForAll] Created PluginsActivation.json with Trueforce entry."
        exit 0
    }

    $raw = Get-Content $ConfigPath -Raw
    $arr = $raw | ConvertFrom-Json
    # ConvertFrom-Json returns either a single object (if the JSON was a
    # one-element array in some PS versions) or an array. Normalize.
    if ($arr -isnot [System.Array]) { $arr = @($arr) }

    if ($arr | Where-Object { $_.ClassName -eq $class }) {
        Write-Host "[TrueforceForAll] Already registered in PluginsActivation.json; leaving alone."
        exit 0
    }

    # Back up before modifying so a botched edit is recoverable.
    Copy-Item $ConfigPath "$ConfigPath.bak" -Force

    $newEntry = [PSCustomObject]@{
        ClassName              = $class
        IsEnabled              = $true
        ShowInMainMenu         = $true
        ShowInMainMenuPosition = 0
    }
    $newArr = @($arr) + $newEntry
    Save-Json $newArr $ConfigPath
    Write-Host "[TrueforceForAll] Added Trueforce entry to PluginsActivation.json."
    exit 0
}
catch {
    Write-Host "[TrueforceForAll] Failed to update PluginsActivation.json: $_"
    # Don't fail the install over this — the user can still enable via
    # SimHub's Add/remove feature button. Exit 0 so [Run] doesn't show
    # an error to the user.
    exit 0
}
