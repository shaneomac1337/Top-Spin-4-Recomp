# Packages a release: one folder, and a zip of it, with what a player unzips and runs.
#
#   pwsh scripts/package.ps1 [-Label rc1] [-Configuration RelWithDebInfo]
#
# Needs a finished build (scripts/build.ps1 -Action Build: the game, the Launcher beside it,
# data/pipeline-keys.txt copied) and the XenosRecomp build (scripts/build-xenosrecomp.ps1).
# The folder holds the game exe and its DLLs, the Visual C++ runtime, the Launcher, the pipeline key list, HELP.md (what Help opens),
# and xenosrecomp\ (the shader converter both programs look for inside the game folder). It
# holds no game data (ADR-0006): the player's disc and Title Update 1 are read at setup, and a
# check below refuses to package anything that looks like game data or a player's files.
param(
    [string]$Label = '',
    [ValidateSet('RelWithDebInfo', 'Release', 'Debug')][string]$Configuration = 'RelWithDebInfo'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot

$version = ([xml](Get-Content -Raw "$projectRoot/launcher/Directory.Build.props")).Project.PropertyGroup.Version | Select-Object -First 1
$name = "TopSpin4Recomp-$version" + ($(if ($Label) { "-$Label" } else { '' }))
$build = "$projectRoot/out/build/win-amd64-$($Configuration.ToLowerInvariant())"
$tools = "$projectRoot/.tools"
$release = "$projectRoot/out/release"
$folder = "$release/$name"

# What goes in: game folder files from the build, then the converter and the licences.
$gameFiles = @('topspin4.exe', 'topspin4-launcher.exe', 'pipeline-keys.txt', 'HELP.md', 'sounds/achievement.wav') +
    @(Get-ChildItem -LiteralPath $build -Filter '*.dll' | ForEach-Object Name)
. "$PSScriptRoot/ConverterFiles.ps1"
$converterFiles = Get-ConverterFiles $projectRoot
# The Microsoft Visual C++ runtime goes beside both programs, so a PC without it still starts them.
$runtimeDlls = @('vcruntime140.dll', 'vcruntime140_1.dll', 'msvcp140.dll', 'msvcp140_atomic_wait.dll')
$installation = & "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe" -latest -products * -property installationPath
$runtimeFolder = Get-ChildItem -Path "$installation/VC/Redist/MSVC/*/x64/Microsoft.VC*.CRT" -Directory |
    Sort-Object { [version]$_.Parent.Parent.Name } | Select-Object -Last 1
if (!$runtimeFolder) { throw "No Visual C++ redistributable under $installation/VC/Redist/MSVC" }
$licences = @{
    'ReXGlue-LICENSE.txt'     = "$tools/rexglue-source/LICENSE"
    'XenosRecomp-LICENSE.md'  = "$tools/xenosrecomp/LICENSE.md"
    'fmt-LICENSE.txt'         = "$tools/xenosrecomp/thirdparty/fmt/LICENSE"
    'xxHash-LICENSE.txt'      = "$tools/xenosrecomp/thirdparty/xxHash/LICENSE"
    'zstd-LICENSE.txt'        = "$tools/xenosrecomp/thirdparty/zstd/LICENSE"
}

foreach ($file in $gameFiles) {
    if (!(Test-Path -LiteralPath "$build/$file")) { throw "Missing from the build: $build/$file (run scripts/build.ps1 -Action Build)" }
}
foreach ($source in @($converterFiles.Values) + @($licences.Values) + @($runtimeDlls | ForEach-Object { "$($runtimeFolder.FullName)/$_" })) {
    if (!(Test-Path -LiteralPath $source)) { throw "Missing: $source" }
}

if (Test-Path -LiteralPath $folder) { Remove-Item -LiteralPath $folder -Recurse -Force }
New-Item -ItemType Directory -Force "$folder/xenosrecomp", "$folder/licences", "$folder/sounds" | Out-Null
foreach ($file in $gameFiles) { Copy-Item -LiteralPath "$build/$file" "$folder/$file" }
foreach ($entry in $converterFiles.GetEnumerator()) { Copy-Item -LiteralPath $entry.Value "$folder/xenosrecomp/$($entry.Key)" }
foreach ($entry in $licences.GetEnumerator()) { Copy-Item -LiteralPath $entry.Value "$folder/licences/$($entry.Key)" }
foreach ($dll in $runtimeDlls) {
    Copy-Item -LiteralPath "$($runtimeFolder.FullName)/$dll" "$folder/$dll"
    Copy-Item -LiteralPath "$($runtimeFolder.FullName)/$dll" "$folder/xenosrecomp/$dll"
}

# Nothing from the disc, no player files: only the kinds of file listed above, and the project's
# own achievement sound (data/sounds).
$allowed = '\.(exe|dll|txt|md|h)$'
$sound = [IO.Path]::GetFullPath("$folder/sounds/achievement.wav")
$unexpected = Get-ChildItem -LiteralPath $folder -Recurse -File |
    Where-Object { ($_.Name -notmatch $allowed -and $_.FullName -ne $sound) -or $_.FullName -match '\\(gamedata|updatedata|userdata|cache|logs)\\' }
if ($unexpected) { throw "Refusing to package: $($unexpected.FullName -join ', ')" }

$zip = "$release/$name.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path $folder -DestinationPath $zip -CompressionLevel Optimal
$size = [math]::Round((Get-Item -LiteralPath $zip).Length / 1MB, 1)
Write-Output "Release folder: $folder"
Write-Output "Release zip: $zip ($size MB)"
