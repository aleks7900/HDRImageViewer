param(
    [ValidateSet('x64')]
    [string[]]$Platforms = @('x64'),
    [string]$Configuration = 'Release',
    [string]$VcpkgRoot = ''
)

$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$nativeRoot = Join-Path $repo 'native\HdrImageViewer.Native'

# Release builds use the same pinned codec stack as the shipped CLI bundle.
# An explicit VcpkgRoot retains the legacy developer build path.
if ([string]::IsNullOrWhiteSpace($VcpkgRoot) -and $Configuration -eq 'Release') {
    $msysRoot = if ($env:HDRIV_MSYS2_ROOT) { $env:HDRIV_MSYS2_ROOT } else { 'C:\msys64' }
    & python (Join-Path $PSScriptRoot 'build-codecs.py') --msys $msysRoot --apply
    if ($LASTEXITCODE -ne 0) { throw "Pinned codec build failed (exit $LASTEXITCODE)" }
    & (Join-Path $PSScriptRoot 'verify-codecs.ps1')
    if ($LASTEXITCODE -ne 0) { throw "Pinned codec verification failed (exit $LASTEXITCODE)" }
    return
}

function Resolve-VcpkgRoot {
    param([string]$ExplicitRoot)

    $candidates = @(
        $ExplicitRoot,
        $env:VCPKG_ROOT,
        $env:VCPKG_INSTALLATION_ROOT,
        'C:\vcpkg',
        (Join-Path $env:USERPROFILE 'vcpkg')
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    foreach ($candidate in $candidates) {
        $exe = Join-Path $candidate 'vcpkg.exe'
        $toolchain = Join-Path $candidate 'scripts\buildsystems\vcpkg.cmake'
        if ((Test-Path $exe) -and (Test-Path $toolchain)) {
            return (Resolve-Path $candidate).Path
        }
    }

    throw 'vcpkg was not found. Install vcpkg or pass -VcpkgRoot. GitHub-hosted Windows runners expose VCPKG_INSTALLATION_ROOT.'
}

function Get-VcpkgTriplet {
    param([string]$Platform)

    switch ($Platform.ToLowerInvariant()) {
        'x64' { return 'x64-windows' }
        default { throw "Unsupported native platform: $Platform" }
    }
}

if (-not (Test-Path $nativeRoot)) {
    throw "Native project not found: $nativeRoot"
}

$resolvedVcpkgRoot = Resolve-VcpkgRoot $VcpkgRoot
$vcpkg = Join-Path $resolvedVcpkgRoot 'vcpkg.exe'
$toolchain = Join-Path $resolvedVcpkgRoot 'scripts\buildsystems\vcpkg.cmake'

Write-Host "vcpkg: $vcpkg"
Write-Host "native: $nativeRoot"

foreach ($platform in $Platforms) {
    $triplet = Get-VcpkgTriplet $platform
    $buildDir = Join-Path $nativeRoot "build\$platform"
    $cachePath = Join-Path $buildDir 'CMakeCache.txt'

    Write-Host ''
    Write-Host "== Native $platform ($triplet) =="

    & $vcpkg install "openexr:$triplet"
    if ($LASTEXITCODE -ne 0) { throw "vcpkg install openexr:$triplet failed (exit $LASTEXITCODE)" }

    if (Test-Path $cachePath) {
        $cache = Get-Content -Raw -Path $cachePath
        if (-not $cache.Contains('vcpkg.cmake')) {
            Write-Host "Removing non-vcpkg CMake cache: $buildDir"
            Remove-Item -LiteralPath $buildDir -Recurse -Force
        }
    }

    & cmake -S $nativeRoot -B $buildDir -A $platform `
        -DCMAKE_TOOLCHAIN_FILE="$toolchain" `
        -DVCPKG_TARGET_TRIPLET="$triplet" `
        -DVCPKG_APPLOCAL_DEPS=OFF
    if ($LASTEXITCODE -ne 0) { throw "cmake configure failed for $platform (exit $LASTEXITCODE)" }

    & cmake --build $buildDir --config $Configuration
    if ($LASTEXITCODE -ne 0) { throw "native build failed for $platform (exit $LASTEXITCODE)" }

    $nativeDll = Join-Path $buildDir "$Configuration\HdrImageViewer.Native.dll"
    if (-not (Test-Path $nativeDll)) {
        throw "Native DLL was not produced: $nativeDll"
    }
}
