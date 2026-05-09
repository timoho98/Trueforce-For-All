# One-off probe: scan installed AC cars and report how often we can
# auto-detect cylinder count from ui_car.json (tags + description).
# This is throwaway — used to decide what hit rate is achievable before
# we commit to building a runtime scanner into the plugin.

$root = "D:\SteamLibrary\steamapps\common\assettocorsa\content\cars"
$cars = Get-ChildItem -Path $root -Directory -ErrorAction SilentlyContinue

# Match tag-style strings: V8, V12, I6, L4, F6, B4, W12, S4 etc.
# Layouts seen in AC tags: V (vee), I/L (inline), F/B (flat/boxer), W, S (single/straight),
# R (rotary, treated specially), H (H-block, rare).
$tagPattern = '^(?<layout>[VILFBWS])(?<count>2|3|4|5|6|8|10|12|16)$'

# Description regex looks for explicit phrases.
# Examples: "V8", "V-8", "5.0L V8", "750-horsepower V12", "3.7-liter V6",
# "inline 6", "inline-six", "flat-6", "boxer 4", "I6 engine", "twin-turbo V8"
$descLayoutPattern = '(?i)\b(V|I|L|F|B|W)[-\s]?(2|3|4|5|6|8|10|12|16)\b'
$descWordPattern = '(?i)\b(?:inline|straight|flat|boxer)[-\s](?:two|three|four|five|six|eight|ten|twelve|2|3|4|5|6|8|10|12)\b'
# "4-cylinder", "6 cylinder", "V8 cylinder layout" etc. The leading char class
# avoids matching "valves per cylinder" (preceded by "per ").
$descCylWordPattern = '(?i)(?<![a-z])(\d{1,2})[-\s]?cylinder\b'
$wordToNum = @{
    'two' = 2; 'three' = 3; 'four' = 4; 'five' = 5; 'six' = 6;
    'eight' = 8; 'ten' = 10; 'twelve' = 12;
}

# Engine codename → cylinder count. These are families known by their
# cylinder layout regardless of chassis. Order matters — longer/more-specific
# names first so "2JZ-GTE" matches before generic "2JZ".
# Sources: well-documented car-enthusiast knowledge of common JDM/USDM swaps.
$engineCodenames = @(
    # JDM straight-six (I6)
    @{ Pattern = '\b2JZ(?:[-\s]?GTE|[-\s]?GE)?\b'; Cylinders = 6 }
    @{ Pattern = '\b1JZ(?:[-\s]?GTE|[-\s]?GE)?\b'; Cylinders = 6 }
    @{ Pattern = '\bRB2[567](?:DET{1,2})?\b';      Cylinders = 6 }
    @{ Pattern = '\bRB30\b';                       Cylinders = 6 }
    @{ Pattern = '\b7M[-\s]?GTE\b';                Cylinders = 6 }
    @{ Pattern = '\bM5[02]B[0-9]+\b';              Cylinders = 6 } # BMW M50/M52
    @{ Pattern = '\bS5[024]B[0-9]+\b';             Cylinders = 6 } # BMW S50/S52/S54
    @{ Pattern = '\bN5[24]B[0-9]+\b';              Cylinders = 6 } # BMW N52/N54
    @{ Pattern = '\bB58\b';                        Cylinders = 6 }
    # JDM straight-four (I4)
    @{ Pattern = '\b4[AG][-\s]?GE\b';              Cylinders = 4 } # Toyota 4A-GE
    @{ Pattern = '\bSR20(?:DET|VE|DE)?\b';         Cylinders = 4 }
    @{ Pattern = '\bCA18(?:DET|DE)?\b';            Cylinders = 4 }
    @{ Pattern = '\bKA24(?:DE|E)?\b';              Cylinders = 4 }
    @{ Pattern = '\b4G6[34]\b';                    Cylinders = 4 } # Mitsubishi 4G63/4G64
    @{ Pattern = '\bK20[ACZ]?\b';                  Cylinders = 4 } # Honda K20
    @{ Pattern = '\bK24[AZ]?\b';                   Cylinders = 4 }
    @{ Pattern = '\bB1[68][AB][0-9]?\b';           Cylinders = 4 } # Honda B-series
    @{ Pattern = '\bF20C\b';                       Cylinders = 4 }
    @{ Pattern = '\bH22A\b';                       Cylinders = 4 }
    @{ Pattern = '\b3SGTE\b';                      Cylinders = 4 }
    @{ Pattern = '\bS14[BE][0-9]+\b';              Cylinders = 4 } # BMW S14 (E30 M3)
    # Subaru flat-four (F4)
    @{ Pattern = '\bEJ20[57]?\b';                  Cylinders = 4 }
    @{ Pattern = '\bEJ25[57]?\b';                  Cylinders = 4 }
    @{ Pattern = '\bFA20\b';                       Cylinders = 4 }
    # USDM V8
    @{ Pattern = '\bLS[1-9]X?\b';                  Cylinders = 8 } # LS1, LS2, LS3, LS7, LSX
    @{ Pattern = '\b\b5\.3[\s-]?LS\b';             Cylinders = 8 }
    @{ Pattern = '\bLT[1-5]\b';                    Cylinders = 8 } # LT1, LT4, etc.
    @{ Pattern = '\bCoyote\b';                     Cylinders = 8 } # Ford 5.0 Coyote
    @{ Pattern = '\bHEMI\b';                       Cylinders = 8 }
    @{ Pattern = '\bHellcat\b';                    Cylinders = 8 }
    # Nissan V6
    @{ Pattern = '\bVQ3[57]\b';                    Cylinders = 6 } # 350Z/G35 VQ35DE, 370Z VQ37VHR
    @{ Pattern = '\bVR38\b';                       Cylinders = 6 } # GT-R R35 (V6 twin-turbo)
    # Porsche flat-6
    @{ Pattern = '\bMezger\b';                     Cylinders = 6 }
)

# Brand+model chassis-code lookup (used as a final fallback). Conservative —
# only entries where the stock engine is unambiguous AND swaps are uncommon
# in AC modding. Users can override anyway.
# Pattern is matched against name OR carId (case-insensitive).
$chassisLookup = @(
    # BMW chassis codes — straight-6 by default, but LS swaps exist (we accept the false positive risk; user can override)
    @{ Pattern = '\bE3[06]\b|\bE3[06][_\s-]'; Cylinders = 6 }  # E30 (most M20/M40 are I6 or I4)
    @{ Pattern = '\bE36\b|\bE36[_\s-]';       Cylinders = 6 }  # E36 — usually I6 (M50/S50)
    @{ Pattern = '\bE46\b|\bE46[_\s-]';       Cylinders = 6 }  # E46 — I6 default, M3 = S54 I6
    @{ Pattern = '\bE9[02]\b|\bE9[02][_\s-]'; Cylinders = 6 }  # E90/E92 — N52/N54 I6, M3 E92 = V8 (false positive)
    @{ Pattern = '\bF8[02]\b|\bF8[02][_\s-]'; Cylinders = 6 }  # F80/F82 M3/M4 — S55 I6
    @{ Pattern = '\bG8[02]\b|\bG8[02][_\s-]'; Cylinders = 6 }  # G80/G82 M3/M4 — S58 I6
    # Nissan
    @{ Pattern = '\b350Z\b|\bfairlady[_\s]?350|\bz33\b'; Cylinders = 6 }  # VQ35
    @{ Pattern = '\b370Z\b|\bz34\b';                     Cylinders = 6 }  # VQ37
    @{ Pattern = '\b300Z\b|\bz3[12]\b';                  Cylinders = 6 }  # VG30
    @{ Pattern = '\b240(?:sx|z)\b';                      Cylinders = 4 }  # KA24 (240SX); 240Z is I6 — kept undetected for safety
    @{ Pattern = '\bskyline[_\s]?gtr?[_\s]?r3[234]|\bbnr3[24]\b|\bbcnr33\b|\b\(bcnr-?33\)|\br3[234][_\s-]?gtr?\b'; Cylinders = 6 }
    @{ Pattern = '\b(?:r3[1234]|hr3[12]|hcr3[12])\b';    Cylinders = 6 }  # Skyline non-GTR sedans (RB-series)
    @{ Pattern = '\bs1[345]\b|\bsilvia[_\s]?s1[345]';    Cylinders = 4 }  # S13/S14/S15 Silvia (CA18/SR20)
    @{ Pattern = '\b180sx\b|\bsil80\b|\bsileighty\b';    Cylinders = 4 }  # CA18/SR20
    @{ Pattern = '\blaurel\b|\bcefiro\b|\bstagea\b';     Cylinders = 6 }  # RB-series Nissan sedans/wagons
    # Mazda
    @{ Pattern = '\bmiata\b|\bmx[-_\s]?5\b|\bmx5\b';     Cylinders = 4 }
    @{ Pattern = '\brx[-_\s]?[78]\b';                    Cylinders = -1 } # rotary
    # Subaru
    @{ Pattern = '\bimpreza|\bwrx\b|\bsti\b|\bgrb\b|\bgda\b|\bgdb\b'; Cylinders = 4 } # F4 boxer
    @{ Pattern = '\bbrz\b|\bgt86\b|\bft86\b|\b86\b';     Cylinders = 4 } # F4 (FA20)
    # Toyota
    @{ Pattern = '\bsupra[_\s]?(?:mk4|a80)|\bjza80\b';   Cylinders = 6 } # 2JZ
    @{ Pattern = '\bsupra[_\s]?(?:mk5|a90|gr)\b';        Cylinders = 6 } # B58
    @{ Pattern = '\b(?:celica[_\s]?)?supra[_\s]?(?:mk2|a60)|\bma6[01]\b'; Cylinders = 6 }  # 5M-GE I6
    @{ Pattern = '\bae86\b|\bcorolla[_\s]?levin|\btrueno'; Cylinders = 4 } # 4A-GE
    @{ Pattern = '\bjzx[789]\d\b|\bcresta\b|\bchaser\b|\bsoarer\b|\bmark[_\s]?ii\b'; Cylinders = 6 }  # 1JZ/2JZ
    # Mitsubishi
    @{ Pattern = '\b(?:lancer[_\s])?evo(?:lution)?[_\s]?(?:[ivx]+|\d+)\b|\bevo[_\s]?[ivx]+\b'; Cylinders = 4 } # 4G63
    # Misc British / European
    @{ Pattern = '\bmini[_\s]?(?:cooper|hatch|clubman|countryman)\b|\baustin[_\s]?mini\b'; Cylinders = 4 }
    # Misc USDM
    @{ Pattern = '\bviper\b|\brt[/_\s-]?10\b';            Cylinders = 10 }
    @{ Pattern = '\bf-?150\b|\bsilverado\b|\bram[_\s]?(?:1500|2500)\b'; Cylinders = 8 }
    # Porsche
    @{ Pattern = '\b911\b|\bcayman\b|\bboxster\b|\b993\b|\b996\b|\b997\b|\b991\b|\b992\b|\b964\b'; Cylinders = 6 } # F6
    # Honda
    @{ Pattern = '\bs2000\b|\bap[12]\b';                 Cylinders = 4 } # F20C/F22C
    @{ Pattern = '\bcivic\b|\bintegra\b|\btype[-_\s]?r\b'; Cylinders = 4 }
    @{ Pattern = '\bnsx\b';                              Cylinders = 6 } # original NSX = C30A V6
    # Ford / Chevy / Dodge — usually V8 in AC mods, but very mixed
    @{ Pattern = '\bmustang\b';                          Cylinders = 8 } # majority V8, ecoboost is I4
    @{ Pattern = '\bcamaro\b|\bcorvette\b|\bvette\b|\bc[5678]\b'; Cylinders = 8 }
    @{ Pattern = '\bchallenger\b|\bcharger\b';           Cylinders = 8 }
)

$stats = @{
    Total              = 0
    HasUiCar           = 0
    DetectedByTag      = 0
    DetectedByDesc     = 0
    DetectedByCylWord  = 0
    DetectedByCodename = 0
    DetectedByChassis  = 0
    Detected           = 0
    Rotary             = 0
    Electric           = 0
    Disagreed          = 0
}

$results = @()
$disagreements = @()

foreach ($car in $cars) {
    $stats.Total++

    $uiPath = Join-Path $car.FullName "ui\ui_car.json"
    if (-not (Test-Path $uiPath)) { continue }
    $stats.HasUiCar++

    try {
        # AC's ui_car.json sometimes has stray bytes — read raw and try parse.
        $raw = Get-Content -Raw -LiteralPath $uiPath -ErrorAction Stop
        # Strip BOM if present
        if ($raw.Length -gt 0 -and $raw[0] -eq [char]0xFEFF) { $raw = $raw.Substring(1) }
        $ui = $raw | ConvertFrom-Json -ErrorAction Stop
    } catch {
        continue
    }

    $cylFromTag = $null
    $layoutFromTag = $null

    if ($ui.tags) {
        foreach ($tag in $ui.tags) {
            if ($null -eq $tag) { continue }
            $t = "$tag"
            # Special cases
            if ($t -match '(?i)^rotary$|^wankel$') {
                $stats.Rotary++
                $cylFromTag = -1  # sentinel for rotary
                break
            }
            if ($t -match '(?i)^electric$|^ev$|^bev$') {
                $stats.Electric++
                $cylFromTag = -2
                break
            }
            if ($t -match $tagPattern) {
                $cylFromTag = [int]$Matches.count
                $layoutFromTag = $Matches.layout
                break
            }
        }
    }

    $cylFromDesc = $null
    $layoutFromDesc = $null
    $cylFromCylWord = $null
    $cylFromCodename = $null
    $codenameMatched = $null

    $desc = ""
    if ($ui.description) {
        $desc = "$($ui.description)" -replace '&[a-z]+;', ' '

        # Layout+number patterns (V8, I6, etc.)
        $m = [regex]::Match($desc, $descLayoutPattern)
        while ($m.Success) {
            $layout = $m.Groups[1].Value.ToUpper()
            $count = [int]$m.Groups[2].Value
            if ($layout -in @('V','I','L','F','B','W')) {
                $cylFromDesc = $count
                $layoutFromDesc = $layout
                break
            }
            $m = $m.NextMatch()
        }

        if ($null -eq $cylFromDesc) {
            $m = [regex]::Match($desc, $descWordPattern)
            if ($m.Success) {
                $word = $m.Value.ToLower() -replace '[-\s]', ' '
                foreach ($key in $wordToNum.Keys) {
                    if ($word -match "\b$key\b") { $cylFromDesc = $wordToNum[$key]; break }
                }
                if ($null -eq $cylFromDesc -and $word -match '\b(\d+)\b') { $cylFromDesc = [int]$Matches[1] }
            }
        }

        # "X-cylinder" / "X cylinder" pattern — strong signal
        $m = [regex]::Match($desc, $descCylWordPattern)
        if ($m.Success) {
            $cylFromCylWord = [int]$m.Groups[1].Value
        }
    }

    # Combined search corpus for codenames and chassis lookups: name + tags + desc
    $haystack = ""
    if ($ui.name) { $haystack += " " + $ui.name }
    if ($ui.tags) { $haystack += " " + ($ui.tags -join " ") }
    $haystack += " " + $desc
    $haystack += " " + $car.Name  # carId itself

    foreach ($cn in $engineCodenames) {
        if ([regex]::IsMatch($haystack, $cn.Pattern)) {
            $cylFromCodename = $cn.Cylinders
            $codenameMatched = $cn.Pattern
            break
        }
    }

    $cylFromChassis = $null
    foreach ($ch in $chassisLookup) {
        if ([regex]::IsMatch($haystack, "(?i)$($ch.Pattern)")) {
            $cylFromChassis = $ch.Cylinders
            break
        }
    }

    # Priority order: explicit numeric (cyl word) > tag layout > desc layout > codename > chassis
    $detected = $null
    $source = $null
    if ($null -ne $cylFromCylWord -and $cylFromCylWord -ge 2 -and $cylFromCylWord -le 16) {
        $detected = $cylFromCylWord
        $source = "cylword:$cylFromCylWord"
        $stats.DetectedByCylWord++
    } elseif ($null -ne $cylFromTag -and $cylFromTag -gt 0) {
        $detected = $cylFromTag
        $source = "tag:$layoutFromTag$cylFromTag"
        $stats.DetectedByTag++
    } elseif ($null -ne $cylFromDesc) {
        $detected = $cylFromDesc
        $source = "desc:$layoutFromDesc$cylFromDesc"
        $stats.DetectedByDesc++
    } elseif ($null -ne $cylFromCodename -and $cylFromCodename -gt 0) {
        $detected = $cylFromCodename
        $source = "codename:$codenameMatched"
        $stats.DetectedByCodename++
    } elseif ($null -ne $cylFromChassis -and $cylFromChassis -gt 0) {
        $detected = $cylFromChassis
        $source = "chassis"
        $stats.DetectedByChassis++
    } elseif ($cylFromTag -in @(-1, -2) -or $cylFromChassis -eq -1) {
        $detected = if ($cylFromTag -eq -2) { -2 } else { -1 }
        $source = if ($detected -eq -1) { "rotary" } else { "electric" }
    }

    if ($null -ne $detected) {
        $stats.Detected++
    }

    if ($cylFromTag -gt 0 -and $cylFromDesc -gt 0 -and $cylFromTag -ne $cylFromDesc) {
        $stats.Disagreed++
        $disagreements += [pscustomobject]@{
            CarId = $car.Name
            TagSays = "$layoutFromTag$cylFromTag"
            DescSays = "$layoutFromDesc$cylFromDesc"
        }
    }

    $results += [pscustomobject]@{
        CarId    = $car.Name
        Cylinders = $detected
        Source   = $source
    }
}

Write-Host ""
Write-Host "=== AC Cylinder Detection Probe ===" -ForegroundColor Cyan
Write-Host ("Total cars scanned:      {0}" -f $stats.Total)
Write-Host ("Has ui_car.json:         {0}  ({1:p1})" -f $stats.HasUiCar, ($stats.HasUiCar / [math]::Max($stats.Total,1)))
Write-Host ("Detected (any source):   {0}  ({1:p1})" -f $stats.Detected, ($stats.Detected / [math]::Max($stats.Total,1)))
Write-Host ("  via cylinder word:     {0}" -f $stats.DetectedByCylWord)
Write-Host ("  via tags:              {0}" -f $stats.DetectedByTag)
Write-Host ("  via description:       {0}" -f $stats.DetectedByDesc)
Write-Host ("  via engine codename:   {0}" -f $stats.DetectedByCodename)
Write-Host ("  via chassis lookup:    {0}" -f $stats.DetectedByChassis)
Write-Host ("  rotary:                {0}" -f $stats.Rotary)
Write-Host ("  electric:              {0}" -f $stats.Electric)
Write-Host ("Disagreements (tag/desc):{0}" -f $stats.Disagreed)
Write-Host ""

if ($disagreements.Count -gt 0) {
    Write-Host "Disagreements (first 20):" -ForegroundColor Yellow
    $disagreements | Select-Object -First 20 | Format-Table -AutoSize
}

# Distribution of detected cylinder counts
Write-Host "Detected cylinder distribution:" -ForegroundColor Cyan
$results | Where-Object { $_.Cylinders -gt 0 } |
    Group-Object Cylinders | Sort-Object @{Expression={[int]$_.Name}} |
    Format-Table @{n='Cylinders';e={$_.Name}}, Count -AutoSize

# Sample undetected
$undetected = $results | Where-Object { $null -eq $_.Cylinders }
Write-Host ("Undetected sample (first 30 of {0}):" -f $undetected.Count) -ForegroundColor Yellow
$undetected | Select-Object -First 30 -ExpandProperty CarId

# Save full results to CSV for inspection
$csvPath = Join-Path $PSScriptRoot "ac_cylinders_probe.csv"
$results | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding utf8
Write-Host ""
Write-Host "Full results written to: $csvPath" -ForegroundColor Green
