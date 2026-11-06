# The shader converter's files as the game folder holds them (xenosrecomp\ beside topspin4.exe,
# where the Launcher's XenosRecompConverter.ShippedWith looks), and where each comes from.
# One list for scripts/package.ps1 (the release) and scripts/build.ps1 (the dev build folder), so
# the two folders hold the same converter (a release also adds the Visual C++ runtime beside it).
#
#   . "$PSScriptRoot/ConverterFiles.ps1"; Get-ConverterFiles $projectRoot

function Get-ConverterFiles([string]$ProjectRoot) {
    $built = "$ProjectRoot/out/build/xenosrecomp/XenosRecomp"  # scripts/build-xenosrecomp.ps1
    [ordered]@{
        'XenosRecomp.exe' = "$built/XenosRecomp.exe"
        'dxcompiler.dll'  = "$built/dxcompiler.dll"
        'fmt.dll'         = "$built/fmt.dll"
        'shader_common.h' = "$ProjectRoot/.tools/xenosrecomp/XenosRecomp/shader_common.h"
    }
}

# Copies the converter into <GameFolder>\xenosrecomp. Returns the sources that are missing (none
# copied then), so the caller decides whether that is fatal.
function Copy-ConverterFiles([string]$ProjectRoot, [string]$GameFolder) {
    $files = Get-ConverterFiles $ProjectRoot
    $missing = @($files.Values | Where-Object { !(Test-Path -LiteralPath $_) })
    if ($missing) { return $missing }
    New-Item -ItemType Directory -Force "$GameFolder/xenosrecomp" | Out-Null
    foreach ($entry in $files.GetEnumerator()) {
        Copy-Item -LiteralPath $entry.Value "$GameFolder/xenosrecomp/$($entry.Key)" -Force
    }
    return @()
}
