# Builds the controller and the 3D shell into .\bin.
# Uses a portable .NET SDK in .\.tools\dotnet if present, otherwise `dotnet` on PATH.
# Pass -Restart to stop running SLW3D (or legacy Desktop3D) processes before publishing and restart the controller after.
param(
    [switch]$Restart
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

$localDotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
if (Test-Path $localDotnet) {
    $dotnet = $localDotnet
    $env:NUGET_PACKAGES = Join-Path $root '.tools\nuget'
} else {
    $dotnet = 'dotnet'
}

$procNames = @('SLW3D.Controller', 'SLW3D.Shell', 'Desktop3D.Controller', 'Desktop3D.Shell')
$running = Get-Process -Name $procNames -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase) }
if ($running) {
    if (-not $Restart) {
        $names = ($running | Select-Object -ExpandProperty Name -Unique) -join ', '
        throw "Cannot publish while running: $names. Exit from the tray menu first, or re-run with: .\build.ps1 -Restart"
    }
    Write-Host "Stopping running SLW3D processes cleanly ..."
    foreach ($evtName in @('SLW3D.Controller.Quit', 'Desktop3D.Controller.Quit')) {
        try {
            $ev = [System.Threading.EventWaitHandle]::OpenExisting($evtName)
            $null = $ev.Set()
            $ev.Dispose()
            Start-Sleep -Milliseconds 1200
        } catch { }
    }
    $still = Get-Process -Name $procNames -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase) }
    if ($still) {
        $still | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 400
    }
}

foreach ($p in @(
    @{ proj = 'controller\Controller.csproj'; out = 'bin\controller' },
    @{ proj = 'shell\Shell.csproj';           out = 'bin\shell' }
)) {
    Write-Host "Building $($p.proj) ..."
    & $dotnet publish (Join-Path $root $p.proj) -c Release -o (Join-Path $root $p.out) --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $($p.proj)" }
}

Write-Host "Build OK"

if ($Restart) {
    $exe = Join-Path $root 'bin\controller\SLW3D.Controller.exe'
    Write-Host "Starting controller ..."
    Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe)
}
