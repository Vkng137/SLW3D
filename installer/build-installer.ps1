# Builds self-contained win-x64 binaries, runs unit tests, creates the Inno Setup installer, and writes SHA256SUMS.txt.
param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

# Strip optional leading 'v' from git tag versions (e.g. v1.0.0 -> 1.0.0)
$cleanVersion = $Version.TrimStart('v', 'V')

$localDotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
if (Test-Path $localDotnet) {
    $dotnet = $localDotnet
    $env:NUGET_PACKAGES = Join-Path $root '.tools\nuget'
} else {
    $dotnet = 'dotnet'
}

Write-Host "Running unit tests ..."
& $dotnet run --project (Join-Path $root 'tests\SLW3D.Tests.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "Unit tests failed" }

$distDir = Join-Path $root 'dist'
$stageDir = Join-Path $distDir 'stage'
if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
New-Item -ItemType Directory -Path $stageDir -Force | Out-Null

foreach ($p in @(
    @{ proj = 'controller\Controller.csproj'; out = 'bin\controller' },
    @{ proj = 'shell\Shell.csproj';           out = 'bin\shell' }
)) {
    Write-Host "Publishing $($p.proj) (win-x64, self-contained) ..."
    $outDir = Join-Path $stageDir $p.out
    & $dotnet publish (Join-Path $root $p.proj) `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:Version=$cleanVersion `
        -p:DebugType=none `
        -p:DebugSymbols=false `
        -o $outDir `
        --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $($p.proj)" }
}

Write-Host "Staging assets, web UI, and clean default config ..."
Copy-Item (Join-Path $root 'PROJECT.md') (Join-Path $stageDir 'PROJECT.md')
Copy-Item (Join-Path $root 'shell\web') (Join-Path $stageDir 'shell\web') -Recurse

New-Item -ItemType Directory -Path (Join-Path $stageDir 'assets\backgrounds') -Force | Out-Null
Copy-Item (Join-Path $root 'assets\slw3d.ico') (Join-Path $stageDir 'assets\slw3d.ico')
Copy-Item (Join-Path $root 'assets\slw3d.png') (Join-Path $stageDir 'assets\slw3d.png')
Copy-Item (Join-Path $root 'assets\backgrounds\.gitkeep') (Join-Path $stageDir 'assets\backgrounds\.gitkeep')
Copy-Item (Join-Path $root 'assets\backgrounds\CityScape.png') (Join-Path $stageDir 'assets\backgrounds\CityScape.png')

New-Item -ItemType Directory -Path (Join-Path $stageDir 'config') -Force | Out-Null
foreach ($cfg in @('apps.json', 'shell.json', 'themes.json', 'controller.example.json')) {
    Copy-Item (Join-Path $root "config\$cfg") (Join-Path $stageDir "config\$cfg")
}

$isccCandidates = @(
    (Get-Command iscc -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe',
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) }

$iscc = $isccCandidates | Select-Object -First 1
if (-not $iscc) {
    throw "Inno Setup 6 compiler (ISCC.exe) not found. Install JRSoftware.InnoSetup before building the installer."
}

$issPath = Join-Path $PSScriptRoot 'SLW3D.iss'
Write-Host "Compiling installer with $iscc ..."
& $iscc "/DMyAppVersion=$cleanVersion" $issPath
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE" }

$exeName = "SLW3D-Setup-$cleanVersion-win-x64.exe"
$exePath = Join-Path $distDir $exeName
if (-not (Test-Path $exePath)) { throw "Expected installer not found: $exePath" }

$hash = (Get-FileHash -Path $exePath -Algorithm SHA256).Hash.ToLowerInvariant()
$sumPath = Join-Path $distDir 'SHA256SUMS.txt'
Set-Content -Path $sumPath -Value "$hash  $exeName" -Encoding Ascii

Write-Host ""
Write-Host "Installer : $exePath"
Write-Host "Size      : $([Math]::Round((Get-Item $exePath).Length / 1MB, 2)) MB"
Write-Host "SHA-256   : $hash"
Write-Host "Checksums : $sumPath"
