# Convert the shader containers captured with --ts4_shader_dump_dir into the Native renderer's
# shader pack, which the game loads at startup from its cache folder (default: cache, the
# cache_root the dev scripts pass; scene runs pass --ts4_shader_pack). Every folder and file
# here holds game-derived data and stays local. Copied from WWE All Stars (ADR-0004).
param([string]$InputDirectory = 'cache/guest-shaders',
      [string]$OutputPath = 'cache/topspin4.shaderpack')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Set-Location $projectRoot
$tool = 'out/build/xenosrecomp/XenosRecomp/XenosRecomp.exe'
if (!(Test-Path $tool)) { & "$PSScriptRoot/build-xenosrecomp.ps1" | Out-Null }
if (!(Get-ChildItem $InputDirectory -Filter '*.bin' -ErrorAction SilentlyContinue)) {
    throw "No shader containers in $InputDirectory. Run the game once with --ts4_renderer=classic --ts4_shader_dump_dir=$InputDirectory (without a shader pack, Native ends the session)."
}
New-Item -ItemType Directory -Force cache/shader-native | Out-Null
& $tool $InputDirectory cache/shader-native/shader_cache.cpp .tools/xenosrecomp/XenosRecomp/shader_common.h cache/shader-native/hlsl *> cache/shader-native/run.log
if ($LASTEXITCODE -ne 0) { throw "XenosRecomp failed; see cache/shader-native/run.log" }
# The XenosRecomp patch series the shaders were converted with (CMakeLists.txt computes the
# same id); a build configured with another series treats the pack as stale and will not start.
$series = [Text.StringBuilder]::new()
foreach ($patch in Get-ChildItem patches/xenosrecomp -Filter '*.patch' | Sort-Object Name) {
    $null = $series.Append([Convert]::ToHexString([IO.File]::ReadAllBytes($patch.FullName)).ToLower())
}
$sha = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::ASCII.GetBytes($series.ToString()))
$converterId = [Convert]::ToHexString($sha).Substring(0, 16).ToLower()
$outputDirectory = Split-Path $OutputPath -Parent
if ($outputDirectory) { New-Item -ItemType Directory -Force $outputDirectory | Out-Null }
# The pack also holds the texture slots each shader reads, so draws bind only those.
& python "$PSScriptRoot/shader_pack.py" cache/shader-native/shader_cache.cpp cache/shader-native/hlsl $converterId $OutputPath
if ($LASTEXITCODE -ne 0) { throw "shader_pack.py failed" }
Write-Output "Shader pack written: $OutputPath"
