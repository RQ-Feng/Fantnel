# ============================================================
#  Fantnel - one-shot local runner
#    1) build backend (Fantnel.slnx)
#    2) build web UI + deploy to <out>/resources/static
#    3) start Fantnel.exe
#
#  Usage:
#    .\start.ps1                    # full build then start on port 13521
#    .\start.ps1 -Port 13600        # custom port (app auto-shifts if busy)
#    .\start.ps1 -NoBuild           # just start (use existing build output)
#    .\start.ps1 -SkipWeb           # build backend only, keep current web UI
#    .\start.ps1 -WithResourceUpdate  # also allow remote resource (7z/CRT) updates
#
#  NOTE: the remote "Fantnel UI" package update has been removed, so the web
#  UI that gets served is exactly what you build here. Run without -SkipWeb
#  /-NoBuild at least once, otherwise resources/static stays empty.
# ============================================================

param(
    [int]$Port = 13521,
    [switch]$NoBuild,
    [switch]$SkipWeb,
    [switch]$WithResourceUpdate
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out  = Join-Path $root 'Fantnel\bin\Debug\net10.0'
$web  = Join-Path $root 'web'
$static = Join-Path $out 'resources\static'

function Info([string]$msg) { Write-Host $msg -ForegroundColor Cyan }

if (-not $NoBuild) {
    Info '[1/3] building backend ...'
    dotnet build (Join-Path $root 'Fantnel.slnx') -v minimal | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'backend build failed' }
}

if (-not $NoBuild -and -not $SkipWeb) {
    Info '[2/3] building web UI ...'
    Push-Location $web
    try {
        if (-not (Test-Path (Join-Path $web 'node_modules'))) {
            Info '      node_modules missing -> npm install'
            npm install
            if ($LASTEXITCODE -ne 0) { throw 'npm install failed' }
        }
        npm run build
        if ($LASTEXITCODE -ne 0) { throw 'web build failed' }
    } finally {
        Pop-Location
    }

    Info '[3/3] deploying web UI to resources/static ...'
    # tips: plain /MIR would delete resources/static/image (not part of vite dist)
    if (Test-Path (Join-Path $static 'assets')) {
        Remove-Item (Join-Path $static 'assets') -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $static | Out-Null
    robocopy (Join-Path $web 'dist') $static /E /NFL /NDL /NJH /NJS | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "deploy failed (robocopy exit $LASTEXITCODE)" }
    $global:LASTEXITCODE = 0
}

if (-not (Test-Path (Join-Path $static 'index.html'))) {
    Write-Warning 'resources/static/index.html is missing - run the script once without -NoBuild/-SkipWeb.'
}

$busy = Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue | Select-Object -First 1
if ($busy) {
    $owner = Get-Process -Id $busy.OwningProcess -ErrorAction SilentlyContinue
    Write-Warning ("port {0} is busy: PID {1} {2} - Fantnel will auto-pick the next free port" -f $Port, $busy.OwningProcess, $owner.Path)
}

$args = @('--fantnel_port', "$Port")
if (-not $WithResourceUpdate) {
    # skip remote "static.win" (7z.exe / CRT dll) and "Resource System" downloads while developing
    $args += @('--update_static_false', '--update_static_system_false')
}

if (-not (Test-Path (Join-Path $out 'Fantnel.exe'))) { throw "Fantnel.exe not found: $out - build first" }

Write-Host ("starting Fantnel on port {0} (Ctrl+C to stop) ..." -f $Port) -ForegroundColor Green
Set-Location $out
& (Join-Path $out 'Fantnel.exe') @args
