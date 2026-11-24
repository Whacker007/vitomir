# Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
# Free for noncommercial use, never to be sold, never shipped with the original game's files.
# Builds a developer's platform zip (release 1's package is tools\package.ps1, the base game alone): a VitomirPlatform\ folder
# holding bin\ (vitomir-platform.exe, self-contained for 64-bit Windows, so no .NET install is needed,
# vitomir-platform-cli.exe, the same program for a console, and vitomir-launcher.exe, the launcher a player starts
# from; its
# modules as folders in bin\Modules\ — the enhanced module in bin\Modules\Enhanced\, its module.json, its DLL
# and its rules data — and its games in bin\Games\) and a read-me. It unpacks beside the faithful game's folder and never into it:
#
#   powershell -ExecutionPolicy Bypass -File tools\package-platform.ps1
#   Expand-Archive out\package-platform\VitomirPlatform-<version>.zip -DestinationPath C:\Games -Force
#
# **No game content goes in it**: the program reads the player's own 1.1 install where it is. The script
# refuses a package any of the game's file types reached.
#
# The developer's tools, vitomir-tools (src\Vitomir.Tools), are not a player's and stay out of this
# package; a developer's copy is published beside it by hand.
#
# The models repository's people the program draws (the player's body) are copied from the models
# repository (-ModelsRepo, VITOMIR_MODELS_DIR by default) into VitomirPlatform\People\, the files tools\platform\people-files.txt
# lists, with People\source.json (the repository, its commit, the files); without the repository the package is made without them.
param([string]$Out = "", [string]$ModelsRepo = $env:VITOMIR_MODELS_DIR)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
# The release version (src\Directory.Build.props), and the development batch beside it when the tree has build\batch.props
# (the version line's bracket; a public tree has none).
$version = (Select-String -Path (Join-Path $repo "src\Directory.Build.props") -Pattern '<Version>([^<]+)</Version>').Matches[0].Groups[1].Value
$batchProps = Join-Path $repo "build\batch.props"
$batch = $null
if (Test-Path $batchProps) { $batch = (Select-String -Path $batchProps -Pattern '<VitomirBatch>([^<]+)</VitomirBatch>').Matches[0].Groups[1].Value }
if (-not $Out) { $Out = Join-Path $repo "out\package-platform" }
$stage = Join-Path $Out "VitomirPlatform"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }        # the last package's staging folder, ours alone

dotnet publish (Join-Path $repo "src\Vitomir.Platform.Host\Vitomir.Platform.Host.csproj") -c Release -r win-x64 --self-contained true -o (Join-Path $stage "bin")
if ($LASTEXITCODE -ne 0) { throw "publish failed" }
# The same program for a console (vitomir-platform-cli.exe), beside it: a console waits for it and shows its log.
dotnet publish (Join-Path $repo "src\Vitomir.Platform.Cli\Vitomir.Platform.Cli.csproj") -c Release -r win-x64 --self-contained true -o (Join-Path $stage "bin")
if ($LASTEXITCODE -ne 0) { throw "publish of the console program failed" }
# The launcher (Play, Setup, Modules, Folders, About), beside them: it starts vitomir-platform.exe from its own folder.
dotnet publish (Join-Path $repo "src\Vitomir.Launcher\Vitomir.Launcher.csproj") -c Release -r win-x64 --self-contained true -o (Join-Path $stage "bin")
if ($LASTEXITCODE -ne 0) { throw "publish of the launcher failed" }
# The publish lays the modules out (src\Vitomir.ModuleLayout.targets); a package without them would refuse to start.
foreach ($need in "bin\vitomir-platform.exe", "bin\vitomir-platform-cli.exe", "bin\vitomir-launcher.exe", "bin\Modules\Enhanced\module.json", "bin\Modules\Enhanced\bin\Vitomir.Modules.Enhanced.dll", "bin\Games\enhanced.json") {
    if (-not (Test-Path (Join-Path $stage $need))) { throw "the package has no $need" }
}
Copy-Item (Join-Path $PSScriptRoot "package-platform\README.txt") (Join-Path $stage "README.txt")
# The people, beside bin\ (the program looks for People\ there after --models, its setting and VITOMIR_MODELS_DIR).
& (Join-Path $PSScriptRoot "platform\package-people.ps1") -ModelsRepo $ModelsRepo -Destination (Join-Path $stage "People")
# A packaged program sits outside its clone, so a scenario's results could not name the source they
# ran. The clone's state at packaging goes beside the program (bin\build-info.json: commit, branch, whether the tree is dirty and the
# SHA-256 of `git diff HEAD`'s bytes, as the runner hashes a live diff; no path of this machine); the runner reads it when it finds
# no clone around itself.
try {
    $commit = (& git -C $repo rev-parse HEAD).Trim()
    $branch = (& git -C $repo rev-parse --abbrev-ref HEAD).Trim()
    $diffFile = Join-Path ([System.IO.Path]::GetTempPath()) "vitomir-package-diff-$PID.txt"
    & git -C $repo diff HEAD --output=$diffFile
    $dirty = (Test-Path $diffFile) -and ((Get-Item $diffFile).Length -gt 0)
    $diffSha = $null
    if ($dirty) { $diffSha = (Get-FileHash $diffFile -Algorithm SHA256).Hash.ToLowerInvariant() }
    if (Test-Path $diffFile) { Remove-Item $diffFile -Force }
    $info = [ordered]@{ format = "vitomir-build-info/1"; version = $version; batch = $batch; commit = $commit; branch = $branch; dirty = $dirty; diffSha256 = $diffSha; packagedUtc = (Get-Date).ToUniversalTime().ToString("o") }
    [System.IO.File]::WriteAllText((Join-Path $stage "bin\build-info.json"), ($info | ConvertTo-Json))
    Write-Host "build info: $commit ($branch)$(if ($dirty) { ', dirty ' + $diffSha.Substring(0, 12) } else { '' })"
} catch { Write-Host "build info: not written (no git here: $($_.Exception.Message)); a scenario's results will not name the source" }

# vitomir-tools is not a player's: the package refuses it (and the retired old program, its library and the developer's tools by
# any of their files' names). PackageContentsTests holds the two publishes above to projects that reach neither.
$notPlayers = Get-ChildItem $stage -Recurse -File | Where-Object { $_.Name -match '^(vitomir|vitomir-tools|Vitomir\.Tools)(\.(exe|dll|pdb|deps\.json|runtimeconfig\.json))?$' }
if ($notPlayers) { throw "not a player's program in the package: $(($notPlayers | ForEach-Object FullName) -join ', ')" }

$ini = Get-ChildItem $stage -Recurse -File | Where-Object { $_.Name -ieq "vitomir.ini" }
if ($ini) { throw "a vitomir.ini in the package (the player's own, never shipped): $(($ini | ForEach-Object FullName) -join ', ')" }

$gameTypes = '.nob','.nil','.nod','.nad','.nsd','.not','.nsc','.nsw','.nsg','.tga','.bik','.wav','.mp3','.nlp','.java','.class'
$bad = Get-ChildItem $stage -Recurse -File | Where-Object { $gameTypes -contains $_.Extension.ToLowerInvariant() }
if ($bad) { throw "game content in the package: $(($bad | ForEach-Object FullName) -join ', ')" }

$zip = Join-Path $Out "VitomirPlatform-$version$(if ($batch) { '-' + $batch } else { '' }).zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $stage -DestinationPath $zip
Write-Host "package: $zip ($([math]::Round((Get-Item $zip).Length / 1MB)) MB)"
