param(
    # Codegen translates the Modules to C++ in generated/ (needs the player's game files).
    # Launcher publishes the preparation console tool (topspin4-setup) to out/launcher/publish
    # and the Avalonia Launcher (topspin4-launcher) to out/launcher/publish-app;
    # LauncherTest runs the launcher's xUnit tests. Neither needs game data. Build also runs
    # Launcher afterwards, which copies topspin4-launcher.exe and HELP.md beside topspin4.exe in
    # out/build/<preset>/, so a developer starts the same Launcher a player does.
    # Check runs every test that needs no game data: pytest, the host unit tests (Test), then
    # LauncherTest; with generated code present, also scripts/d3d_callsites.py. Run it before
    # committing. Test builds and runs the host unit tests
    # (tests/host) alone.
    [ValidateSet('Configure', 'Build', 'Codegen', 'Launcher', 'LauncherTest', 'Check', 'Test')][string]$Action = 'Build',
    [string]$VisualStudioRoot = 'C:/Program Files (x86)/Microsoft Visual Studio/18/BuildTools',
    [string]$WindowsSdkRoot = 'C:/Program Files (x86)/Windows Kits/10',
    [ValidateRange(1, 64)][int]$Jobs = 8,
    [ValidateSet('RelWithDebInfo', 'Release', 'Debug')][string]$Configuration = 'RelWithDebInfo',
    # Extra CMake cache entries for Configure.
    [string[]]$CMakeArgs = @()
)
$ErrorActionPreference = 'Stop'
# CMake asks git for version details inside .tools/rexglue-source; with optional locks a
# stopped build can leave that tree's index.lock behind (scripts/apply-patches.ps1).
$env:GIT_OPTIONAL_LOCKS = '0'
. "$PSScriptRoot/Invoke-Hidden.ps1"
$projectRoot = Split-Path $PSScriptRoot -Parent
Set-Location $projectRoot
New-Item -ItemType Directory -Force analysis | Out-Null
if ($Action -eq 'Check') {
    & python -m pytest "$projectRoot/tests" -q
    if ($LASTEXITCODE -ne 0) { throw "pytest failed (exit $LASTEXITCODE)" }
    # The D3D hooks' argument registers against the game's call sites; needs the generated code,
    # which a tree without game data does not have.
    if ((Test-Path "$projectRoot/generated/loader") -and (Test-Path "$projectRoot/generated/swing")) {
        & python "$PSScriptRoot/d3d_callsites.py"
        if ($LASTEXITCODE -ne 0) { throw "d3d_callsites.py flagged hook arguments (exit $LASTEXITCODE)" }
    }
    & $PSCommandPath -Action Test -Jobs $Jobs -Configuration $Configuration
    & $PSCommandPath -Action LauncherTest -Jobs $Jobs
    return
}
if ($Action -in 'Launcher', 'LauncherTest') {
    . "$PSScriptRoot/Use-PinnedDotnet.ps1"
    $dotnet = Use-PinnedDotnet $projectRoot
    if ($Action -eq 'Launcher') {
        Invoke-Hidden $dotnet @('publish', 'src/Launcher.Cli/Launcher.Cli.csproj', '-c', 'Release', '-r', 'win-x64',
            '--self-contained', '-p:PublishSingleFile=true', '-p:DebugType=none',
            '-o', "$projectRoot/out/launcher/publish", "-maxcpucount:$Jobs") `
            -WorkingDirectory "$projectRoot/launcher" -LogPath "$projectRoot/analysis/build-launcher.log"
        Write-Output "Launcher: $projectRoot/out/launcher/publish/topspin4-setup.exe"
        # The Avalonia Launcher (setup and Play), one self-contained exe that sits beside topspin4.exe.
        Invoke-Hidden $dotnet @('publish', 'src/Launcher.App/Launcher.App.csproj', '-c', 'Release', '-r', 'win-x64',
            '--self-contained', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=none',
            '-o', "$projectRoot/out/launcher/publish-app", "-maxcpucount:$Jobs") `
            -WorkingDirectory "$projectRoot/launcher" -LogPath "$projectRoot/analysis/build-launcher-app.log"
        Write-Output "Launcher app: $projectRoot/out/launcher/publish-app/topspin4-launcher.exe"
        # The dev build's exe folder gets the Launcher and HELP.md (Help opens it), so a developer
        # sees what a player sees. Only a build that exists is touched: Build calls this after CMake.
        $buildFolder = "$projectRoot/out/build/win-amd64-$($Configuration.ToLowerInvariant())"
        if (Test-Path -LiteralPath "$buildFolder/topspin4.exe") {
            Copy-Item -LiteralPath "$projectRoot/out/launcher/publish-app/topspin4-launcher.exe" "$buildFolder/topspin4-launcher.exe" -Force
            Copy-Item -LiteralPath "$projectRoot/HELP.md" "$buildFolder/HELP.md" -Force
            Write-Output "Launcher placed beside the game: $buildFolder/topspin4-launcher.exe"
            # And the shader converter, where a release has it (xenosrecomp\), or the Launcher's shader
            # step fails and the game won't start.
            . "$PSScriptRoot/ConverterFiles.ps1"
            $missing = Copy-ConverterFiles $projectRoot $buildFolder
            if ($missing) {
                Write-Warning ("The shader converter is not built, so the Launcher here cannot prepare shaders. " +
                    "Run scripts/build-xenosrecomp.ps1, then build again. Missing: $($missing -join ', ')")
            } else {
                Write-Output "Shader converter placed beside the game: $buildFolder/xenosrecomp"
            }
        }
    } else {
        try {
            Invoke-Hidden $dotnet @('test', 'tests/Launcher.Tests/Launcher.Tests.csproj', '-c', 'Release', "-maxcpucount:$Jobs") `
                -WorkingDirectory "$projectRoot/launcher" -LogPath "$projectRoot/analysis/test-launcher.log"
        } finally {
            # The summary, and on failure the failing tests and build errors.
            Get-Content "$projectRoot/analysis/test-launcher.log" |
                Select-String -Pattern '^(Passed|Failed)!|^\s+Failed |error [A-Z]+\d+' | ForEach-Object { $_.Line }
        }
    }
    Write-Output "$Action completed. Logs: $projectRoot/analysis/"
    return
}

$sdk = "$projectRoot/.tools/rexglue-0.10.0/win-amd64"
$source = "$projectRoot/.tools/rexglue-source"
$llvm = "$projectRoot/.tools/llvm-21.1.8/bin"
$cmakeRoot = "$VisualStudioRoot/Common7/IDE/CommonExtensions/Microsoft/CMake"
$cmake = "$cmakeRoot/CMake/bin/cmake.exe"
$msvc = Get-ChildItem "$VisualStudioRoot/VC/Tools/MSVC" -Directory |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$windowsVersion = Get-ChildItem "$WindowsSdkRoot/Include" -Directory |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
foreach ($path in @("$sdk/bin/rexglue.exe", "$llvm/clang++.exe", $cmake, "$source/CMakeLists.txt",
    "$cmakeRoot/Ninja/ninja.exe", "$($msvc.FullName)/include/vector",
    "$WindowsSdkRoot/Include/$($windowsVersion.Name)/um/Windows.h")) {
    if (!(Test-Path -LiteralPath $path)) { throw "Missing build prerequisite: $path (run scripts/bootstrap.ps1)" }
}
$revision = (Invoke-Hidden (Get-Command git).Source @('-C', $source, 'rev-parse', 'HEAD') | Out-String).Trim()
if ($revision -ne 'f5337cdc947ff6d4c4196737e2c807a48f2a1fc2') { throw "Unexpected ReXGlue source revision: $revision" }
# Fails if patches/ changed since the tree was patched.
& "$PSScriptRoot/apply-patches.ps1" | Out-Null

$env:PATH = "$llvm;$cmakeRoot/Ninja;$($msvc.FullName)/bin/Hostx64/x64;$WindowsSdkRoot/bin/$($windowsVersion.Name)/x64;$env:PATH"
$env:INCLUDE = "$($msvc.FullName)/include;$WindowsSdkRoot/Include/$($windowsVersion.Name)/ucrt;$WindowsSdkRoot/Include/$($windowsVersion.Name)/shared;$WindowsSdkRoot/Include/$($windowsVersion.Name)/um;$WindowsSdkRoot/Include/$($windowsVersion.Name)/winrt"
$env:LIB = "$($msvc.FullName)/lib/x64;$WindowsSdkRoot/Lib/$($windowsVersion.Name)/ucrt/x64;$WindowsSdkRoot/Lib/$($windowsVersion.Name)/um/x64"

$preset = "win-amd64-$($Configuration.ToLowerInvariant())"
$buildDirectory = "$projectRoot/out/build/$preset"
# Vulkan only (ADR-0005), so the SDK is always built from the patched source tree.
$configureArguments = @('--preset', $preset, '-B', $buildDirectory, "-DREXSDK_DIR=$source",
    '-DREXGLUE_USE_VULKAN=ON', '-DREXGLUE_USE_D3D12=OFF',
    "-DPYTHON_EXECUTABLE=$((Get-Command python.exe -ErrorAction Stop).Source)",
    # The SDK's Windows x64 preset, when compiling it from source.
    '-DCMAKE_C_FLAGS=-march=x86-64-v2', '-DCMAKE_CXX_FLAGS=-march=x86-64-v2') + $CMakeArgs
# FidelityFX headers enable the runtime's CAS sharpening and FSR 1 upscaling output filters.
$fidelityfx = "$projectRoot/.tools/fidelityfx-sdk"
if (Test-Path "$fidelityfx/sdk/include") { $configureArguments += "-DREXGLUE_FIDELITYFX_SOURCE_DIR=$fidelityfx" }

if ($Action -eq 'Codegen') {
    # The Reference build (ADR-0002): the disc's Modules plus Title Update 1's patches. Codegen
    # applies each .xexp because it sits next to its XEX in assets/.
    $moduleFiles = @(
        @{ File = 'default.xex'; From = 'gamedata'; Sha256 = '2D72E84FEAB9A84AC0E1239785A9AF1ED7B6E693CF39041F206A92AC9454CE28' },
        @{ File = 'Loader_DLL.xex'; From = 'gamedata'; Sha256 = '8F9ED530C9CF32CF2AE33534BA1CA99E3ABDCF2F5859BC8F68658E23187FD092' },
        @{ File = 'Swing_DLL.xex'; From = 'gamedata'; Sha256 = 'DFA71292F75A649773C280DE7BC2688E6E47E473ABA397C248FB4204C45FAC44' },
        @{ File = 'default.xexp'; From = 'updatedata'; Sha256 = '1BA6119F45561CA5B916E3D13DF81B6318B3A9AC89E8FD3DC8BF292D30BD3FC7' },
        @{ File = 'Loader_DLL.xexp'; From = 'updatedata'; Sha256 = '9666DF4D181DB804EB9B59994274A7075475BB5C433E7CB12D116B5FD310F657' },
        @{ File = 'Swing_DLL.xexp'; From = 'updatedata'; Sha256 = 'D5837EF967415ECD77547F3434CF8973F025BE8CB45E398FF7EECFD68A0726ED' }
    )
    New-Item -ItemType Directory -Force assets | Out-Null
    foreach ($moduleFile in $moduleFiles) {
        $target = "assets/$($moduleFile.File)"
        if (!(Test-Path -LiteralPath $target)) {
            $from = "$($moduleFile.From)/$($moduleFile.File)"
            if (!(Test-Path -LiteralPath $from)) { throw "Missing $from. Extract the disc to gamedata/ and Title Update 1 to updatedata/." }
            Copy-Item -LiteralPath $from $target
        }
        if ((Get-FileHash $target -Algorithm SHA256).Hash -ne $moduleFile.Sha256) {
            throw "$target is not the Reference build's $($moduleFile.File) (media 60819732 with Title Update 1)."
        }
    }
    Invoke-Hidden "$sdk/bin/rexglue.exe" @('codegen', 'topspin4_manifest.toml', '--ignore-stamp') -LogPath "$projectRoot/analysis/codegen.log"
} elseif ($Action -eq 'Configure') {
    Invoke-Hidden $cmake $configureArguments -LogPath "$projectRoot/analysis/configure.log"
} elseif ($Action -eq 'Test') {
    Invoke-Hidden $cmake ($configureArguments + '-DTOPSPIN4_BUILD_TESTS=ON') -LogPath "$projectRoot/analysis/configure.log"
    Invoke-Hidden $cmake @('--build', $buildDirectory, '--parallel', "$Jobs", '--target', 'topspin4_host_tests') -LogPath "$projectRoot/analysis/build-tests.log"
    & "$buildDirectory/topspin4_host_tests.exe"
    if ($LASTEXITCODE -ne 0) { throw "Host unit tests failed (exit $LASTEXITCODE)" }
} else {
    Invoke-Hidden $cmake @('--build', $buildDirectory, '--parallel', "$Jobs") -LogPath "$projectRoot/analysis/build.log"
    # Keys on the shipped list whose shaders are in neither the dev shader pack nor the top-ups
    # the game merges into it (a pack is game data, so only a local one is checked).
    $pack = "$projectRoot/cache/topspin4.shaderpack"
    if (Test-Path -LiteralPath $pack) {
        & python "$PSScriptRoot/pipeline_keys.py" check "$projectRoot/data/pipeline-keys.txt" $pack --top-ups "$projectRoot/cache/shader-topup"
    } else {
        Write-Output "Pipeline key list not checked: no shader pack at $pack"
    }
    # The published Launcher goes beside topspin4.exe in the build output.
    & $PSCommandPath -Action Launcher -Jobs $Jobs -Configuration $Configuration
}
Write-Output "$Action completed. Logs: $projectRoot/analysis/"
