param()

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$shaderDirectory = Join-Path $repoRoot 'Rendering\Shaders'
$sourcePath = Join-Path $shaderDirectory 'GainMap.hlsl'
$kitsBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'

if (-not (Test-Path $sourcePath)) {
    throw "Shader source not found: $sourcePath"
}

$fxc = Get-ChildItem -Path $kitsBin -Filter fxc.exe -File -Recurse |
    Where-Object { $_.Directory.Name -eq 'x64' } |
    Sort-Object {
        $version = $null
        if ([Version]::TryParse($_.Directory.Parent.Name, [ref]$version)) {
            $version
        }
        else {
            [Version]'0.0'
        }
    } -Descending |
    Select-Object -First 1

if ($null -eq $fxc) {
    throw "fxc.exe was not found under $kitsBin"
}

$shaders = @(
    @{
        EntryPoint = 'VSMain'
        Profile = 'vs_5_0'
        Output = 'GainMap.VS.cso'
    },
    @{
        EntryPoint = 'PSMain'
        Profile = 'ps_5_0'
        Output = 'GainMap.PS.cso'
    },
    @{
        EntryPoint = 'BaseImagePSMain'
        Profile = 'ps_5_0'
        Output = 'BaseImage.PS.cso'
    }
)

foreach ($shader in $shaders) {
    $outputPath = Join-Path $shaderDirectory $shader.Output
    $arguments = @(
        '/nologo',
        '/WX',
        '/O3',
        '/Qstrip_debug',
        '/Qstrip_reflect',
        '/T', $shader.Profile,
        '/E', $shader.EntryPoint,
        '/Fo', $outputPath,
        $sourcePath
    )

    & $fxc.FullName @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Shader compilation failed for $($shader.EntryPoint) ($($shader.Profile))"
    }

    Write-Host "Compiled $($shader.EntryPoint) -> $outputPath"
}
