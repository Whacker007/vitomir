# Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
# Free for noncommercial use, never to be sold, never shipped with the original game's files.
# Turns one of 1.1's own levels into a cell package the platform plays (ours): a test ground for the
# movement besides the East Gate, with 1.1's streets, stairs and squares (by design).
#
#   powershell -ExecutionPolicy Bypass -File tools\platform\cook-1-1-level.ps1                 # P1_OTDY, Prague's Old Town by day
#   (the developer's tools, vitomir-tools: -Vitomir, else the repository's build in src\Vitomir.Tools\bin\Debug)
#   powershell -ExecutionPolicy Bypass -File tools\platform\cook-1-1-level.ps1 -Level P1_OLDT  # another level
#   C:\Games\VitomirPlatform\bin\vitomir-platform.exe --level C:\Games\Vitomir\levels\P1_OTDY.l2 --sandbox
#   powershell -ExecutionPolicy Bypass -File tools\platform\cook-1-1-level.ps1 -Free           # the whole level a free place
#
# It uses the developer's tools' commands (vitomir-tools): --export-gltf (the level's geometry and pictures, from the
# player's own install), --convert-l2 (its starts and exits), --things (where 1.1's exit signs stand) and
# --cook-l2 (the cell package), with a layout written here between them: 1.1's player starts, its exits with
# their signs (so each cooked exit has its place and a volume the player walks into), and as walkable ground the
# sectors that hold 1.1's walk nodes. The package holds pictures cooked from the player's install, so it stays on this machine and is
# never put in the repository. 1.1's lightmaps are not used: the modern renderer lights it by its sun.
#
# -Free marks the whole level a free place for the Masquerade: one area,
# "free", over the level's bounds with a wide margin, whose "masquerade" key says "free" (the cook keeps an area's keys
# it does not know; the platform reads them). Its kind is "zone", not "interior", so the renderer does not light it as
# a room, and it opens no exposure. Without -Free the level is unmarked, so public everywhere.
#
# Ground: the cook takes as walkable the materials named by -Ground (words in a material's name), within the
# walk nodes' sectors. P1_OTDY's are its floors and the materials of its stairs and its raised porch; another
# level may need its own (the export's .gltf lists the materials).
param(
    [string]$Level = "P1_OTDY",
    [string]$Out = "C:\Games\Vitomir\levels",
    [string]$Vitomir = "",
    [string]$Ground = "floor,Usteps2,Uwall1_2,wood1",
    [switch]$Free
)
$ErrorActionPreference = "Stop"

# The developer's tools (vitomir-tools): the one given, else the repository's own build.
if ($Vitomir -eq "") {
    $Vitomir = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\src\Vitomir.Tools\bin\Debug\net10.0\vitomir-tools.exe"))
}
if (-not (Test-Path $Vitomir)) { throw "vitomir-tools is not at $Vitomir (build src\Vitomir.Tools, or give -Vitomir)" }
Write-Host "tools: $Vitomir"
$work = Join-Path $Out "$Level-work"
$package = Join-Path $Out "$Level.l2"
foreach ($folder in @($work, $package)) {
    if (Test-Path $folder) { Remove-Item $folder -Recurse -Force }      # this script's own last run
}
New-Item -ItemType Directory -Force $work | Out-Null

function Step([string]$what, [string[]]$arguments) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    & $Vitomir @arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)" }
    Write-Host ("{0}: {1:N1} s" -f $what, $clock.Elapsed.TotalSeconds)
}

$export = Join-Path $work "export"
$convert = Join-Path $work "convert"
Step "export" @("--export-gltf", $Level, $export)
Step "convert" @("--convert-l2", $Level, $convert)

# The layout: 1.1's player starts for this level's own scene and its exits, from the converted manifest
# (already in the cooked frame: metres, Z up), and the walk nodes' sectors from the export.
$manifest = Get-Content (Join-Path $convert "$Level.l2\level.json") -Raw | ConvertFrom-Json
$walk = Get-Content (Join-Path $export "$Level.walk.json") -Raw | ConvertFrom-Json
$starts = @($manifest.anchors | Where-Object { $_.kind -eq "start" -and $_.scene -eq $Level } | ForEach-Object {
    [ordered]@{ kind = "start"; name = $_.name; position = @($_.position); rotation = @($_.rotation) }
})
# 1.1's exit signs: the things with a ClickExit (or RegionExit) script, whose first parameter is the
# number of the exit they take ("script ClickExit (9)" in --things' list); their places, in the cooked frame,
# are the converted manifest's anchors of the same index. Each exit carries its signs as `trackers` and
# `walkIn`, so the cook gives it `at` (the signs' middle) and a volume round it,
# which the platform stops the player at until taking exits is built.
# Standard output only: Windows PowerShell 5.1 turns a native program's error-stream lines into a stop when they are
# redirected and $ErrorActionPreference is Stop, so whatever it says there is left on the console.
$listing = @(& $Vitomir --level $Level --things | ForEach-Object { "$_" })
if ($LASTEXITCODE -ne 0) { throw "things failed (exit code $LASTEXITCODE)" }
$signs = @{}
foreach ($line in $listing) {
    if ($line -notmatch '^\s*(\d+)\s.*\bscript (ClickExit|RegionExit) \((-?\d+)') { continue }
    $index = [int]$Matches[1]; $script = $Matches[2]; $number = [int]$Matches[3]
    $sign = $manifest.anchors | Where-Object { $_.scene -eq $Level -and $_.index -eq $index -and $_.script -eq $script } | Select-Object -First 1
    if ($null -eq $sign) { Write-Host "exit $number`: sign $index ($script) is not among the converted things; left out"; continue }
    if (-not $signs.ContainsKey($number)) { $signs[$number] = New-Object System.Collections.ArrayList }
    [void]$signs[$number].Add(@($sign.position))
}
$exits = @($manifest.exits | ForEach-Object {
    $exit = [ordered]@{ number = $_.number; to = $_.to; toLevel = $_.toLevel; entrance = $_.entrance; closed = $_.closed }
    $at = $signs[[int]$_.number]
    if ($null -ne $at) {
        $exit.trackers = $at
        $exit.walkIn = $true
        if ($at.Count -gt 1) {
            # Two signs (the University's two doors, 8 m apart): one volume over both, with the margins the cook
            # gives one sign (2 m round it, 2.5 m below, 2 m above), rather than the cook's box round their middle.
            $x = $at | ForEach-Object { [double]$_[0] } | Measure-Object -Minimum -Maximum
            $y = $at | ForEach-Object { [double]$_[1] } | Measure-Object -Minimum -Maximum
            $z = $at | ForEach-Object { [double]$_[2] } | Measure-Object -Minimum -Maximum
            $exit.volume = [ordered]@{
                origin = @(($x.Minimum - 2), ($y.Minimum - 2), ($z.Minimum - 2.5)); rotZ = 0
                size = @(($x.Maximum - $x.Minimum + 4), ($y.Maximum - $y.Minimum + 4), ($z.Maximum - $z.Minimum + 4.5))
            }
        }
    }
    else { Write-Host ("exit {0} ({1}) has no sign in the level; it gets no place or volume" -f $_.number, $_.to) }
    $exit
})
$sectors = @($walk.nodes | ForEach-Object { $_.sector } | Sort-Object -Unique | ForEach-Object { "sector_$_" })
if ($starts.Count -eq 0) { throw "$Level has no player starts of its own scene" }
$layout = [ordered]@{ anchors = $starts; exits = $exits; ground = [ordered]@{ walkNodes = $sectors } }
if ($Free) {
    # One area over the whole level (the converted manifest's bounds, metres, Z up), 50 m round it and 100 m below and above.
    $min = @($manifest.bounds.min | ForEach-Object { [double]$_ })
    $max = @($manifest.bounds.max | ForEach-Object { [double]$_ })
    if ($min.Count -ne 3 -or $max.Count -ne 3) { $min = @(-10000, -10000, -10000); $max = @(10000, 10000, 10000) }
    $layout.areas = @([ordered]@{
        name = "free"; kind = "zone"; exposure = 0; masquerade = "free"
        box = [ordered]@{
            origin = @(($min[0] - 50), ($min[1] - 50), ($min[2] - 100)); rotZ = 0
            size = @(($max[0] - $min[0] + 100), ($max[1] - $min[1] + 100), ($max[2] - $min[2] + 200))
        }
    })
    Write-Host "layout: the whole level is a free place for the Masquerade (one area, free)"
}
$layoutPath = Join-Path $work "layout.json"
[IO.File]::WriteAllText($layoutPath, ($layout | ConvertTo-Json -Depth 10))
Write-Host ("layout: {0} starts, {1} exits ({2} with their signs), {3} walk-node sectors" -f $starts.Count, $exits.Count, @($exits | Where-Object { $_.Contains("trackers") }).Count, $sectors.Count)

# The cook's switches come after the output path. --nav-agent human: the navigation mesh is baked for the
# platform's human body (0.3 m round, 1.8 m tall, stepping 0.35 m, walking up to 40 degrees),
# not for the cook's old default agent (0.25 m, 1.7 m, 0.4 m, 45 degrees), so the routes it plans are ones the body can
# take: no riser above its step, no bank steeper than it walks, no squeeze narrower or lower than it is.
$clock = [Diagnostics.Stopwatch]::StartNew()
$said = @(& $Vitomir --cook-l2 (Join-Path $export "$Level.gltf") --layout $layoutPath $package --ground $Ground --no-probes --nav-agent human | ForEach-Object { "$_" })
$said | Out-Host
if ($LASTEXITCODE -ne 0) { throw "cook failed (exit code $LASTEXITCODE)" }
# A faithful older program passes over --nav-agent without a word and bakes for its old agent.
if (-not ($said -match "navigation mesh for an agent")) {
    throw "$Vitomir is too old (it does not know --nav-agent, so the level was baked for the old agent): build the repository, or give -Vitomir a newer vitomir-tools"
}
Write-Host ("cook: {0:N1} s" -f $clock.Elapsed.TotalSeconds)
Write-Host "package: $package"
