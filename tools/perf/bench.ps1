# Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
# Free for noncommercial use, never to be sold, never shipped with the original game's files.
# The measured runs (ours): a fixed set of recordings replayed with
# `--frame-times`, each writing a row for every frame, so that what a frame costs on this machine can be
# read afterwards (tools\perf\frames.py) and laid beside the same runs on another build or another machine.
# It measures and changes nothing: every recording is copied into the run's own folder first, so the
# repository and the player's recordings folder are left exactly as they were.
#
#   powershell -ExecutionPolicy Bypass -File tools\perf\bench.ps1
#
# Each run is the platform's program for a console, vitomir-platform-cli --game classic --developer (the developer's
# switches: --replay, --frame-times), reading the 1.1 the setup imported.
# What it runs, each in a process of its own, as fast as the machine draws (`--replay-fast`: the steps are the
# recording's, so the game played is the same at any pace) unless it says otherwise:
#   s-<name>   the scripted recordings the tests replay (src\Vitomir.Engine.Tests\Replays; not the network pair)
#   c-<name>   the crowds (tools\perf\tapes): 25 to 400 knights set on the party in Vienna, drawn at 2560x1440,
#              and two of them again at 1280x720 — a frame that costs the same at both sizes is the processor's
#   p-<name>   the player's own recordings (-Recordings), menus, loads and all
#   w-<name>   the shortest of those once more at the screen's own pace, as it was played
#
# It leaves, in <Out> (C:\Games\Vitomir\runs\perf\<date>-<time> unless -Out says otherwise):
#   machine.txt       the processor, the memory, the graphics card and its driver, the screen, the build
#   runs.tsv          a line a run: how long it took and how it ended
#   <run>.tsv         the run's frames (`--frame-times`)
#   <run>.txt         what the run said
#   gpu.csv           the graphics card's load a second, when nvidia-smi is there to ask
# Keep the mouse and the keyboard still while it runs (about ten minutes): windows open and close by themselves.
param(
    [string]$Vitomir = "C:\Games\Vitomir\bin\vitomir-platform-cli.exe",
    [string]$Recordings = "C:\Games\Vitomir\runs\recordings",
    [string]$Out = "",
    [string]$Size = "2560x1440",
    [switch]$NoRecordings,          # the scripted recordings and the crowds only
    [string]$Only = ""              # run only the runs whose name holds this
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not (Test-Path $Vitomir)) { throw "no program at $Vitomir (package and install first, or name one with -Vitomir)" }
if (-not $Out) { $Out = Join-Path "C:\Games\Vitomir\runs\perf" (Get-Date -Format "yyyyMMdd-HHmmss") }
$tapes = Join-Path $Out "tapes"
New-Item -ItemType Directory -Force -Path $tapes | Out-Null
$started = Get-Date

# ---- the machine ----------------------------------------------------------------------------------
$about = New-Object System.Collections.Generic.List[string]
$about.Add("measured $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
$about.Add("program  $Vitomir ($((Get-Item $Vitomir).LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')))")
try { $about.Add("repo     $(git -C $repo rev-parse --short HEAD 2>$null) $(git -C $repo log -1 --format=%s 2>$null)") } catch { }
try {
    $os = Get-CimInstance Win32_OperatingSystem
    $about.Add("system   $($os.Caption) $($os.Version)")
    foreach ($c in Get-CimInstance Win32_Processor) {
        $about.Add("cpu      $($c.Name.Trim()): $($c.NumberOfCores) cores, $($c.NumberOfLogicalProcessors) threads, $($c.MaxClockSpeed) MHz")
    }
    $about.Add("memory   $([math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB)) GB")
    foreach ($v in Get-CimInstance Win32_VideoController) {
        $about.Add("gpu      $($v.Name), driver $($v.DriverVersion) ($($v.DriverDate)); the screen $($v.CurrentHorizontalResolution)x$($v.CurrentVerticalResolution) at $($v.CurrentRefreshRate) Hz")
    }
    $about.Add("power    $((powercfg /getactivescheme) -join ' ')")
} catch { $about.Add("machine  not asked: $($_.Exception.Message)") }
[IO.File]::WriteAllLines((Join-Path $Out "machine.txt"), $about)

# ---- the graphics card, a line a second, while the runs go ---------------------------------------
$sampler = $null
$smi = Get-Command nvidia-smi -ErrorAction SilentlyContinue
if ($smi) {
    $smiArgs = "--query-gpu=timestamp,utilization.gpu,utilization.memory,memory.used,power.draw,temperature.gpu --format=csv -l 1 -f `"$(Join-Path $Out 'gpu.csv')`""
    try { $sampler = Start-Process -FilePath $smi.Source -ArgumentList $smiArgs -WindowStyle Hidden -PassThru } catch { $sampler = $null }
}

# ---- one run -------------------------------------------------------------------------------------
$rows = New-Object System.Collections.Generic.List[string]
$rows.Add("run`tkind`ttape`tseconds`texit`tsaid")
function Measure-Run([string]$name, [string]$kind, [string]$tape, [string[]]$extra) {
    if ($Only -and $name -notlike "*$Only*") { return }
    $frames = Join-Path $Out "$name.tsv"
    $said = Join-Path $Out "$name.txt"
    $list = @("--game", "classic", "--developer", "--replay", "`"$tape`"") + $extra + @("--mute", "--no-log-file", "--no-log-console", "--frame-times", "`"$frames`"")
    Write-Host ("{0,-34}" -f $name) -NoNewline
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $Vitomir -ArgumentList ($list -join " ") -Wait -PassThru -NoNewWindow `
            -RedirectStandardOutput $said -RedirectStandardError "$said.err"
    $clock.Stop()
    if ((Test-Path "$said.err") -and (Get-Item "$said.err").Length -gt 0) { Add-Content -Path $said -Value (Get-Content "$said.err") }
    Remove-Item "$said.err" -ErrorAction SilentlyContinue
    # A replay leaves its own digest beside the tape: not wanted here.
    Get-ChildItem $tapes -Filter "*.replay.digest*" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
    $last = ""
    if (Test-Path $said) { $last = (Get-Content $said | Where-Object { $_ -like "replay*" } | Select-Object -Last 1) }
    $n = 0
    if (Test-Path $frames) { $n = [Math]::Max(0, @(Get-Content $frames).Count - 1) }
    $rows.Add("$name`t$kind`t$(Split-Path -Leaf $tape)`t$([math]::Round($clock.Elapsed.TotalSeconds, 1))`t$($p.ExitCode)`t$last")
    [IO.File]::WriteAllLines((Join-Path $Out "runs.tsv"), $rows)
    Write-Host ("{0,7} frames {1,7:0.0} s  exit {2}" -f $n, $clock.Elapsed.TotalSeconds, $p.ExitCode)
}

# ---- the scripted recordings ----------------------------------------------------------------------
$scripted = Join-Path $repo "src\Vitomir.Engine.Tests\Replays"
foreach ($t in Get-ChildItem $scripted -Filter "*.tape" | Where-Object { $_.Name -notlike "net-*" } | Sort-Object Name) {
    Copy-Item $t.FullName $tapes
    Measure-Run "s-$($t.BaseName)" "scripted" (Join-Path $tapes $t.Name) @("--replay-fast")
}

# ---- the crowds ------------------------------------------------------------------------------------
foreach ($t in Get-ChildItem (Join-Path $PSScriptRoot "tapes") -Filter "*.tape" | Sort-Object Name) {
    Copy-Item $t.FullName $tapes
    Measure-Run "c-$($t.BaseName)" "crowd $Size" (Join-Path $tapes $t.Name) @("--replay-fast", "--replay-size", $Size)
    if ($t.BaseName -like "*-100" -or $t.BaseName -like "*-400") {
        Measure-Run "c-$($t.BaseName)-720p" "crowd 1280x720" (Join-Path $tapes $t.Name) @("--replay-fast", "--replay-size", "1280x720")
    }
}

# ---- the player's own recordings ------------------------------------------------------------------
if (-not $NoRecordings -and (Test-Path $Recordings)) {
    $plays = @(Get-ChildItem $Recordings -File | Where-Object { $_.Name -like "*.tape.gz" -or $_.Name -like "*.tape" } | Sort-Object Name)
    foreach ($t in $plays) {
        Copy-Item $t.FullName $tapes
        $saves = ($t.FullName -replace '\.gz$', '') + ".saves"          # the saves a recording began with (TapeSaves.Beside)
        if (Test-Path $saves) { Copy-Item $saves $tapes -Recurse -Force }
        $name = $t.Name -replace '\.tape(\.gz)?$', ''
        Measure-Run "p-$name" "play" (Join-Path $tapes $t.Name) @("--replay-fast")
    }
    # The shortest of them once more at the screen's own pace: how the frames fall when the game waits for the screen.
    $short = $plays | Sort-Object Length | Select-Object -First 1
    if ($short) {
        $name = $short.Name -replace '\.tape(\.gz)?$', ''
        Measure-Run "w-$name" "play, watched" (Join-Path $tapes $short.Name) @()
    }
}

if ($sampler) { try { Stop-Process -Id $sampler.Id -Force -ErrorAction SilentlyContinue } catch { } }
Remove-Item $tapes -Recurse -Force -ErrorAction SilentlyContinue      # the copies; the recordings themselves are where they were
Write-Host ""
Write-Host "measured: $Out  ($([math]::Round(((Get-Date) - $started).TotalMinutes, 1)) minutes)"
