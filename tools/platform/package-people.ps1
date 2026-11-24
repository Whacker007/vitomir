# Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
# Free for noncommercial use, never to be sold, never shipped with the original game's files.
# Copies the models repository's people into a package (the models repository is read from disk
# while developing, and the package copies what is used). Called by tools\package.ps1 -WithPeople and tools\package-platform.ps1; on
# its own:
#
#   powershell -ExecutionPolicy Bypass -File tools\platform\package-people.ps1 -Destination out\package\Vitomir\People
#
# Every file tools\platform\people-files.txt lists (paths relative to the repository) is copied into -Destination keeping its
# relative path, and -Destination\source.json says where they came from: {"repo": ..., "commit": ..., "files": [...]}, the repository
# by its folder's name alone (no path of this machine), the commit from `git -C <repo> rev-parse HEAD` (null when git cannot say). The program finds the folder beside its bin\ (People\) and reads
# the commit from source.json. A repository that is not there, or lacks a listed file, is a warning: no people are copied and the
# package is made without them (the program then draws 1.1's Christof). Windows PowerShell 5.1 and PowerShell 7 alike.
param(
    [string]$ModelsRepo = $env:VITOMIR_MODELS_DIR,
    [Parameter(Mandatory = $true)][string]$Destination,
    [string]$List = ""
)
$ErrorActionPreference = "Stop"
if (-not $List) { $List = Join-Path $PSScriptRoot "people-files.txt" }
$files = @(Get-Content $List | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith("#") })

if (-not (Test-Path (Join-Path $ModelsRepo "assets") -PathType Container)) {
    Write-Warning "people: no models repository at $ModelsRepo (give it with -ModelsRepo): the package is made without people (the player is 1.1's Christof)"
    exit 0
}
$missing = @($files | Where-Object { -not (Test-Path (Join-Path $ModelsRepo $_) -PathType Leaf) })
if ($missing.Count -gt 0) {
    Write-Warning "people: $ModelsRepo lacks $($missing -join ', '): the package is made without people (the player is 1.1's Christof)"
    exit 0
}

if (Test-Path $Destination) { Remove-Item $Destination -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$bytes = 0
foreach ($f in $files) {
    $from = Join-Path $ModelsRepo $f
    $to = Join-Path $Destination $f
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $to) | Out-Null
    Copy-Item $from $to
    $bytes += (Get-Item $to).Length
}

$commit = $null
try {
    $said = & git -C $ModelsRepo rev-parse HEAD 2>$null
    if ($LASTEXITCODE -eq 0 -and $said) { $commit = "$said".Trim() }
} catch { $commit = $null }
$repo = (Resolve-Path $ModelsRepo).Path
$source = [ordered]@{ repo = (Split-Path -Leaf $repo); commit = $commit; files = $files }
[System.IO.File]::WriteAllText((Join-Path $Destination "source.json"), ($source | ConvertTo-Json))
$commitText = "no commit (git could not say)"
if ($commit) { $commitText = "commit $commit" }
Write-Host "people: $($files.Count) files ($([math]::Round($bytes / 1MB)) MB) from $repo, $commitText, into $Destination"
