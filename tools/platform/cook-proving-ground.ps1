# Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
# Free for noncommercial use, never to be sold, never shipped with the original game's files.
# Makes the proving ground (ours): a movement test course for the platform's body, with a lane for every row of
# the movement design's controls table. It is a very specific test
# level, with just enough to include a broad representation of the kinds of structures to be expected.
#
#   powershell -ExecutionPolicy Bypass -File tools\platform\cook-proving-ground.ps1
#   C:\Games\VitomirPlatform\bin\vitomir-platform.exe --level C:\Games\Vitomir\levels\proving-ground.l2 --sandbox --start mantle
#
# It runs the generator, tools\platform\proving-ground\make_course.py (Python 3, the standard library only), which
# writes the course's glTF, its pictures and a layout into <Out>\proving-ground-work, and then the developer's tools'
# cook (vitomir-tools --cook-l2) into <Out>\proving-ground.l2, never into the repository. The navigation mesh is baked for the human
# body (--nav-agent human); every top meant to be stood on is a material whose name holds "ground" (--ground ground),
# every other face a wall.
#
# Python is looked for in this order: -Python <path to python.exe>, the launcher (py -3), python on the PATH, then
# the per-user install's python.exe (%LOCALAPPDATA%\Programs\Python\Python311); the script says which it used.
param(
    [string]$Out = "C:\Games\Vitomir\levels",
    [string]$Vitomir = "",
    [string]$Python = ""
)
$ErrorActionPreference = "Stop"

$generator = Join-Path $PSScriptRoot "proving-ground\make_course.py"
if (-not (Test-Path $generator)) { throw "the generator is not at $generator" }
# The developer's tools that cook (vitomir-tools): the one given, else the repository's own build.
if ($Vitomir -eq "") {
    $Vitomir = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\src\Vitomir.Tools\bin\Debug\net10.0\vitomir-tools.exe"))
}
if (-not (Test-Path $Vitomir)) { throw "vitomir-tools is not at $Vitomir (build src\Vitomir.Tools, or give -Vitomir)" }
Write-Host "cook with: $Vitomir"

# Whether a command runs Python 3: it prints 3 when it does. (The "python" Windows puts on the PATH when no Python is
# installed opens the Store instead and prints nothing.) Standard output only: Windows PowerShell 5.1 turns a native
# program's error-stream lines into a stop when they are redirected and $ErrorActionPreference is Stop.
function Test-Python([string]$exe, [string[]]$first) {
    try {
        $said = & $exe @first -c "import sys; print(sys.version_info[0])"
        return ($LASTEXITCODE -eq 0 -and "$said".Trim() -eq "3")
    }
    catch { return $false }
}

$pythonExe = $null
$pythonFirst = @()
$known = Join-Path $env:LOCALAPPDATA "Programs\Python\Python311\python.exe"
if ($Python -ne "") {
    if (-not (Test-Python $Python @())) { throw "the Python given (-Python $Python) does not run Python 3" }
    $pythonExe = $Python; $how = "$Python (given)"
}
elseif ((Get-Command py -ErrorAction SilentlyContinue) -and (Test-Python "py" @("-3"))) {
    $pythonExe = "py"; $pythonFirst = @("-3"); $how = "the launcher, py -3"
}
elseif ((Get-Command python -ErrorAction SilentlyContinue) -and (Test-Python "python" @())) {
    $pythonExe = "python"; $how = "python on the PATH ($((Get-Command python).Source))"
}
elseif ((Test-Path $known) -and (Test-Python $known @())) {
    $pythonExe = $known; $how = $known
}
if ($null -eq $pythonExe) {
    throw "no Python 3 found: not py -3, not python on the PATH, not $known. Install Python 3.11 or give -Python <path to python.exe>."
}
Write-Host "python: $how"

$work = Join-Path $Out "proving-ground-work"
$package = Join-Path $Out "proving-ground.l2"
foreach ($folder in @($work, $package)) {
    if (Test-Path $folder) { Remove-Item $folder -Recurse -Force }      # this script's own last run
}
New-Item -ItemType Directory -Force $work | Out-Null

$clock = [Diagnostics.Stopwatch]::StartNew()
& $pythonExe @pythonFirst $generator $work | Out-Host
if ($LASTEXITCODE -ne 0) { throw "the generator failed (exit code $LASTEXITCODE)" }
Write-Host ("generator: {0:N1} s" -f $clock.Elapsed.TotalSeconds)

# The cook's switches come after the output path (as tools\platform\cook-1-1-level.ps1's).
$clock = [Diagnostics.Stopwatch]::StartNew()
$said = @(& $Vitomir --cook-l2 (Join-Path $work "proving-ground.gltf") --layout (Join-Path $work "layout.json") $package --ground ground --no-probes --nav-agent human | ForEach-Object { "$_" })
$said | Out-Host
if ($LASTEXITCODE -ne 0) { throw "the cook failed (exit code $LASTEXITCODE)" }
# A faithful older program passes over --nav-agent without a word and bakes for its old agent.
if (-not ($said -match "navigation mesh for an agent")) {
    throw "$Vitomir is too old (it does not know --nav-agent, so the course was baked for the old agent): build the repository, or give -Vitomir a newer vitomir-tools"
}
Write-Host ("cook: {0:N1} s" -f $clock.Elapsed.TotalSeconds)
Write-Host "package: $package"
Write-Host "play it:  vitomir-platform.exe --level $package --sandbox --start <a station: stairs, kerbs, slopes, hop, hurdle, vault, mantle, grab, crouch, doors, gaps, drops, pit, not-built, ladders, corners, leaps, walls, stealth, free-yard, no-climb, no-stand, step-over, sprint-vault>"
