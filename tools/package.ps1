# Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
# Free for noncommercial use, never to be sold, never shipped with the original game's files.
# Builds release 1's package: one zip, Vitomir-<version>-win-x64.zip, that unpacks to one Vitomir\ folder:
#
#   Vitomir\
#     vitomir-launcher.exe   what a player starts (Play, Setup, Modules, Folders, About)
#     README.txt             for the player (tools\package\README.txt)
#     LICENSE.txt            Vitomir's licence (the repository's LICENSE)
#     THIRD_PARTY.txt        what else the package carries, and under which licences (tools\package\THIRD_PARTY.txt)
#     licences\              each of those components' licence in full, and where its source is (tools\package\licences)
#     bin\                   everything else: vitomir-platform.exe (the game), vitomir-platform-cli.exe (the same program for a
#                            console), the Classic module (bin\Modules\Classic\), the game list (bin\Games\classic.json), the
#                            launcher's own files, the .NET runtime (self-contained for 64-bit Windows: no .NET install is
#                            needed), build-info.json and files.sha256
#     People\                only with -WithPeople: the models repository's people (tools\platform\package-people.ps1)
#
# and SHA256SUMS.txt beside the zip (the zip's SHA-256 in the coreutils format, `sha256sum -c SHA256SUMS.txt`). This is the
# developer's step; a player only unzips the folder (to somewhere like C:\Games\Vitomir) and starts vitomir-launcher.exe.
#
#   powershell -ExecutionPolicy Bypass -File tools\package.ps1
#   powershell -ExecutionPolicy Bypass -File tools\package.ps1 -WithPeople -ModelsRepo <the models repository's folder>
#
# **The launcher at the top.** bin\ holds the launcher as it is published (vitomir-launcher.dll and its runtime, shared with the
# game's programs); the vitomir-launcher.exe at the top is the same program's own starter (the .NET app host published beside
# it) with the name of the program it starts changed from vitomir-launcher.dll to bin\vitomir-launcher.dll. The app host finds
# the runtime beside the program it names, so the launcher runs from bin\ (its own folder there, where it finds
# vitomir-platform.exe) on the one runtime of bin\: nothing is duplicated, and the exe at the top is the app host's size
# (a few hundred KB). bin\ keeps no second vitomir-launcher.exe.
#
# **What the package refuses**, each before the zip is made: a stage without the programs, the Classic module or its game; the
# developer's tools (vitomir-tools); a vitomir.ini (the player's own: README.txt shows an example); the Enhanced module and its game (left out of release 1; its code stays in the source,
# and tools\package-platform.ps1 packages it for a developer); a game list naming any game but classic; a people file or a
# People\ folder without -WithPeople; any of the game's file types (no game content: docs\ORIGINAL_CONTENT_POLICY.md); and
# in any text file, a path of this machine (the repository's folder, the user's profile), the user's or the machine's name, or
# an absolute path that is not one a player is told (C:\Games\..., the Program Files folder, and vitomir.ini's example D:\Vitomir-data).
#
# -Runtime names another runtime identifier (linux-x64 for a check in a Linux container: the same layout, the programs without
# .exe and the app host's separator a /). -Out puts the stage and the zip elsewhere (default out\package\).
param(
    [string]$Out = "",
    [switch]$WithPeople,
    [string]$ModelsRepo = $env:VITOMIR_MODELS_DIR,
    [string]$Runtime = "win-x64",
    # The notice after the version line (the game's main menu, the launcher's footer and About) and the packaged README's
    # first line: the build's VitomirNotice (src\Directory.Build.props). "" for none. Public 1.0 changes this default alone.
    [string]$Notice = "Private beta - please do not distribute"
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
# The release version (src\Directory.Build.props), and the development batch for build-info.json when the tree has
# build\batch.props (a public tree has none). The zip is named by the release version alone.
$version = (Select-String -Path (Join-Path $repo "src\Directory.Build.props") -Pattern '<Version>([^<]+)</Version>').Matches[0].Groups[1].Value
$batchProps = Join-Path $repo "build\batch.props"
$batch = $null
if (Test-Path $batchProps) { $batch = (Select-String -Path $batchProps -Pattern '<VitomirBatch>([^<]+)</VitomirBatch>').Matches[0].Groups[1].Value }
if (-not $Out) { $Out = Join-Path $repo "out\package" }
$windows = $Runtime -like "win-*"
$exe = if ($windows) { ".exe" } else { "" }
$stage = Join-Path $Out "Vitomir"
$bin = Join-Path $stage "bin"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }        # the last package's staging folder, ours alone
New-Item -ItemType Directory -Force -Path $bin | Out-Null

# The game (vitomir-platform), the same program for a console (vitomir-platform-cli) and the launcher, self-contained into one
# bin\, each laying the modules out beside it (src\Vitomir.ModuleLayout.targets).
foreach ($project in "Vitomir.Platform.Host", "Vitomir.Platform.Cli", "Vitomir.Launcher") {
    dotnet publish (Join-Path $repo "src\$project\$project.csproj") -c Release -r $Runtime --self-contained true -o $bin "-p:VitomirNotice=$Notice"
    if ($LASTEXITCODE -ne 0) { throw "the publish of $project failed" }
}
# Release 1 is the base game alone: the Enhanced module and its game, which the programs' builds lay out, are taken out of
# the stage (and refused below should one be left).
foreach ($left in "Modules\Enhanced", "Games\enhanced.json") {
    $path = Join-Path $bin $left
    if (Test-Path $path) { Remove-Item $path -Recurse -Force }
}

# The launcher at the top: bin\'s app host with the program it starts changed to bin\vitomir-launcher.dll (above). The name is
# written in place, in the app host's own slot for it (1024 bytes, zero-padded), which must hold it once and nothing after it.
$hostBytes = [IO.File]::ReadAllBytes((Join-Path $bin "vitomir-launcher$exe"))
$latin = [Text.Encoding]::GetEncoding(28591)                         # one char a byte, so a string index is a byte index
$text = $latin.GetString($hostBytes)
$from = "vitomir-launcher.dll"
$to = "bin" + $(if ($windows) { "\" } else { "/" }) + $from
$at = $text.IndexOf($from + [char]0, [StringComparison]::Ordinal)
if ($at -lt 0 -or $text.IndexOf($from + [char]0, $at + 1, [StringComparison]::Ordinal) -ge 0) { throw "the launcher's app host does not name $from once" }
if ($text.Substring($at + $from.Length, $to.Length - $from.Length + 1) -ne ([string][char]0 * ($to.Length - $from.Length + 1))) { throw "the launcher's app host has no room for $to" }
$name = $latin.GetBytes($to)
[Array]::Copy($name, 0, $hostBytes, $at, $name.Length)
[IO.File]::WriteAllBytes((Join-Path $stage "vitomir-launcher$exe"), $hostBytes)
Remove-Item (Join-Path $bin "vitomir-launcher$exe") -Force

# The player's files at the top.
Copy-Item (Join-Path $PSScriptRoot "package\README.txt") (Join-Path $stage "README.txt")
# The notice, when there is one, the packaged README's first line (the copy's: the README in the tree stays without it).
if ($Notice) {
    $readme = Join-Path $stage "README.txt"
    [System.IO.File]::WriteAllText($readme, $Notice + "`n`n" + [System.IO.File]::ReadAllText($readme), (New-Object System.Text.UTF8Encoding($false)))
}
Copy-Item (Join-Path $repo "LICENSE") (Join-Path $stage "LICENSE.txt")
Copy-Item (Join-Path $PSScriptRoot "package\THIRD_PARTY.txt") (Join-Path $stage "THIRD_PARTY.txt")
# Each component's licence in full, with its copyright lines and where its source is (THIRD_PARTY.txt names each file).
Copy-Item (Join-Path $PSScriptRoot "package\licences") (Join-Path $stage "licences") -Recurse

# The people, only when asked (beside bin\, where the program looks for People\).
$peopleList = @(Get-Content (Join-Path $PSScriptRoot "platform\people-files.txt") | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith("#") })
if ($WithPeople) {
    & (Join-Path $PSScriptRoot "platform\package-people.ps1") -ModelsRepo $ModelsRepo -Destination (Join-Path $stage "People")
    if (-not (Test-Path (Join-Path $stage "People\source.json"))) { throw "-WithPeople: the people were not copied (the warning above says why)" }
}

# A packaged program sits outside its clone, so a scenario's results could not name the source they ran: the clone's state at
# packaging goes beside the program (bin\build-info.json: the version, the batch, the commit, the branch, whether the tree is
# dirty and the SHA-256 of `git diff HEAD`'s bytes, as the runner hashes a live diff, and when it was packaged, in UTC), which the
# runner reads when it finds no clone around itself. No path and nothing else of this machine.
try {
    $commit = (& git -C $repo rev-parse HEAD).Trim()
    $branch = (& git -C $repo rev-parse --abbrev-ref HEAD).Trim()
    $diffFile = Join-Path ([System.IO.Path]::GetTempPath()) "vitomir-package-diff-$PID.txt"
    & git -C $repo diff HEAD --output=$diffFile
    $dirty = (Test-Path $diffFile) -and ((Get-Item $diffFile).Length -gt 0)
    $diffSha = $null
    if ($dirty) { $diffSha = (Get-FileHash $diffFile -Algorithm SHA256).Hash.ToLowerInvariant() }
    if (Test-Path $diffFile) { Remove-Item $diffFile -Force }
    $info = [ordered]@{ format = "vitomir-build-info/1"; version = $version; batch = $batch; notice = $Notice; commit = $commit; branch = $branch; dirty = $dirty; diffSha256 = $diffSha; packagedUtc = (Get-Date).ToUniversalTime().ToString("o") }
    [System.IO.File]::WriteAllText((Join-Path $bin "build-info.json"), ($info | ConvertTo-Json))
    Write-Host "build info: $commit ($branch)$(if ($dirty) { ', dirty ' + $diffSha.Substring(0, 12) } else { '' })"
} catch { Write-Host "build info: not written (no git here: $($_.Exception.Message)); a scenario's results will not name the source" }

# ---- the refusals ------------------------------------------------------------------------------------------------------
$files = @(Get-ChildItem $stage -Recurse -File)
function Rel([IO.FileInfo]$f) { $f.FullName.Substring($stage.Length + 1).Replace('\', '/') }

foreach ($need in "vitomir-launcher$exe", "bin\vitomir-launcher.dll", "bin\vitomir-platform$exe", "bin\vitomir-platform-cli$exe", "bin\Modules\Classic\module.json", "bin\Modules\Classic\bin\Vitomir.Modules.Classic.dll", "bin\Games\classic.json", "README.txt", "LICENSE.txt", "THIRD_PARTY.txt", "licences\dotnet.txt", "licences\IKVM.txt", "licences\ECJ.txt") {
    if (-not (Test-Path (Join-Path $stage $need))) { throw "the package has no $need" }
}
# Not a player's: the developer's tools (and the old program, by any of its files' names).
$notPlayers = $files | Where-Object { $_.Name -match '^(vitomir|vitomir-tools|Vitomir\.Tools)(\.(exe|dll|pdb|deps\.json|runtimeconfig\.json))?$' }
if ($notPlayers) { throw "not a player's program in the package: $(($notPlayers | ForEach-Object { Rel $_ }) -join ', ')" }
# Release 1 carries the base game alone.
$enhanced = $files | Where-Object { (Rel $_) -match '(^|/)Modules/Enhanced/' -or $_.Name -like "Vitomir.Modules.Enhanced.*" }
if ($enhanced) { throw "the Enhanced module in the package: $(($enhanced | ForEach-Object { Rel $_ }) -join ', ')" }
# The Classic module's Java runtime image (IKVM's ikvm\<runtime>\ and its native library) is the one for the machine that built
# the module, so a package for another runtime than the building machine's would carry the wrong one: build it on that runtime.
$ikvm = Join-Path $bin "Modules\Classic\bin\ikvm"
if (Test-Path $ikvm) {
    $images = @(Get-ChildItem $ikvm -Directory | ForEach-Object Name)
    if ($images -notcontains $Runtime) { throw "the Classic module's IKVM image is for $($images -join ', '), not ${Runtime}: build the package on a $Runtime machine" }
}
$games = @(Get-ChildItem (Join-Path $bin "Games") -File | ForEach-Object Name)
if (($games -join ",") -ne "classic.json") { throw "the game list is not classic alone: $($games -join ', ')" }
# The people only with -WithPeople: no People\ folder, and none of the listed people's files anywhere, without it.
if (-not $WithPeople) {
    $names = @($peopleList | ForEach-Object { Split-Path -Leaf $_ })
    $people = $files | Where-Object { (Rel $_) -match '(^|/)People/' -or $names -contains $_.Name }
    if ($people -or (Test-Path (Join-Path $stage "People"))) { throw "people in the package without -WithPeople: $(($people | ForEach-Object { Rel $_ }) -join ', ')" }
}
# No vitomir.ini: it is the player's own (where the user folder is), so a new version unzipped over the folder never overwrites it.
$ini = $files | Where-Object { $_.Name -ieq "vitomir.ini" -or $_.Name -ieq "vitomir.ini.previous" }
if ($ini) { throw "a vitomir.ini in the package (the player's own, never shipped): $(($ini | ForEach-Object { Rel $_ }) -join ', ')" }
# No game content.
$gameTypes = '.nob','.nil','.nod','.nad','.nsd','.not','.nsc','.nsw','.nsg','.tga','.bik','.wav','.mp3','.nlp','.java','.class'
$bad = $files | Where-Object { $gameTypes -contains $_.Extension.ToLowerInvariant() }
if ($bad) { throw "game content in the package: $(($bad | ForEach-Object { Rel $_ }) -join ', ')" }
# No path of this machine, no user's or machine's name, no absolute path but a player's generic ones, in any text file.
$textTypes = '.txt','.json','.md','.xml','.config','.ini','.csv','.tsv','.ps1','.py','.sh','.html','.css','.props','.targets'
$mine = @($repo, $HOME, $env:USERPROFILE, $env:LOCALAPPDATA, $env:APPDATA) | Where-Object { $_ -and $_.Length -ge 4 } | ForEach-Object { $_.TrimEnd('\', '/') }
$common = 'root','user','users','admin','administrator','owner','default','guest','localhost','workgroup','runner','vitomir'
$names = @($env:USERNAME, $env:USER, $env:COMPUTERNAME, [Environment]::MachineName, [Environment]::UserName) | Where-Object { $_ -and $_.Length -ge 4 -and $common -notcontains $_.ToLowerInvariant() } | Select-Object -Unique
$absolute = '(?<![\w%])([A-Za-z]:[\\/][^\s"''<>|*?]*|\\\\[A-Za-z0-9._-]+\\[^\s"''<>|]*|/(home|root|Users|mnt|tmp|sessions|var|opt)/[^\s"''<>|]*)'
$generic = '^(?i)(C:[\\/](Games([\\/]|$)|Program Files)|D:[\\/]Vitomir-data$)'   # the README's examples (vitomir.ini's too)
$leaks = New-Object System.Collections.Generic.List[string]
foreach ($f in $files | Where-Object { $textTypes -contains $_.Extension.ToLowerInvariant() }) {
    $body = [IO.File]::ReadAllText($f.FullName)
    foreach ($m in $mine) { if ($body.IndexOf($m, [StringComparison]::OrdinalIgnoreCase) -ge 0) { $leaks.Add("$(Rel $f): this machine's path") } }
    foreach ($n in $names) { if ($body -match "(?i)(?<![A-Za-z0-9])$([regex]::Escape($n))(?![A-Za-z0-9])") { $leaks.Add("$(Rel $f): a user's or the machine's name") } }
    foreach ($m in [regex]::Matches($body, $absolute)) { if ($m.Value -notmatch $generic) { $leaks.Add("$(Rel $f): an absolute path, $($m.Value)") } }
}
if ($leaks.Count -gt 0) { throw "this machine in the package's text files: $(($leaks | Select-Object -Unique) -join '; ')" }

# The package's own list of its files (bin\files.sha256: each file's SHA-256 and its path in Vitomir\, the coreutils format),
# for the launcher or the setup to check later.
$sums = $files | Sort-Object { Rel $_ } | ForEach-Object { "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $(Rel $_)" }
[IO.File]::WriteAllText((Join-Path $bin "files.sha256"), (($sums -join "`n") + "`n"))

# The zip (entries with /, the Vitomir\ folder at its root) and its SHA-256 beside it.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipName = "Vitomir-$version-$Runtime.zip"
$zip = Join-Path $Out $zipName
if (Test-Path $zip) { Remove-Item $zip -Force }
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
$zipSha = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $Out "SHA256SUMS.txt"), "$zipSha  $zipName`n")
$unzipped = ($files | Measure-Object Length -Sum).Sum + (Get-Item (Join-Path $bin "files.sha256")).Length
Write-Host "package: $zip ($([math]::Round((Get-Item $zip).Length / 1MB)) MB zipped, $([math]::Round($unzipped / 1MB)) MB unzipped, $($files.Count + 1) files); SHA-256 $zipSha; notice: $(if ($Notice) { $Notice } else { 'none' })"
