<#
.SYNOPSIS
    Master build script — bundles the Photobooth app into a single .exe.

.DESCRIPTION
    Step 1: Build the Python FastAPI service into python_service.exe (PyInstaller).
    Step 2: Publish the WPF Desktop app as a self-contained single-file exe
            with python_service.exe embedded as a resource.

    Output: dist\Photobooth.Desktop.exe  (single file, no dependencies required)

.EXAMPLE
    .\build_all.ps1
    .\build_all.ps1 -SkipPythonBuild   # Re-use existing python_service.exe
#>

param(
    [switch]$SkipPythonBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot      = $PSScriptRoot
$PythonSvcDir  = Join-Path $RepoRoot "python-service"
$DesktopDir    = Join-Path $RepoRoot "Photobooth.Desktop"
$DistDir       = Join-Path $RepoRoot "dist"
$ResourcesDir  = Join-Path $DesktopDir "Resources"
$PythonExe     = Join-Path $ResourcesDir "python_service.exe"

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host "════════════════════════════════════════════" -ForegroundColor DarkCyan
    Write-Host "  $Message" -ForegroundColor Cyan
    Write-Host "════════════════════════════════════════════" -ForegroundColor DarkCyan
}

# ── Step 1: Build Python service ───────────────────────────────────────────
if ($SkipPythonBuild) {
    Write-Step "Step 1/2: Skipping Python build (-SkipPythonBuild flag set)"
    if (-not (Test-Path $PythonExe)) {
        Write-Error "python_service.exe not found at '$PythonExe'. Run without -SkipPythonBuild first."
    }
}
else {
    Write-Step "Step 1/2: Building Python service → python_service.exe"
    Push-Location $PythonSvcDir
    try {
        & powershell.exe -ExecutionPolicy Bypass -File ".\build_service.ps1"
        if ($LASTEXITCODE -ne 0) { throw "build_service.ps1 failed with exit code $LASTEXITCODE" }
    }
    finally {
        Pop-Location
    }

    if (-not (Test-Path $PythonExe)) {
        Write-Error "python_service.exe was not produced at '$PythonExe'."
    }
    Write-Host "✓ python_service.exe ready ($([math]::Round((Get-Item $PythonExe).Length / 1MB, 1)) MB)" -ForegroundColor Green
}

# ── Step 2: Publish WPF app as single file ─────────────────────────────────
Write-Step "Step 2/2: Publishing WPF app → single-file exe"

if (Test-Path $DistDir) {
    Remove-Item $DistDir -Recurse -Force
}

Push-Location $DesktopDir
try {
    dotnet publish `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        --output $DistDir `
        --nologo

    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
}
finally {
    Pop-Location
}

# ── Summary ────────────────────────────────────────────────────────────────
$OutputExe = Join-Path $DistDir "Photobooth.Desktop.exe"
if (-not (Test-Path $OutputExe)) {
    Write-Error "Build failed — output exe not found at '$OutputExe'."
}

$SizeMb = [math]::Round((Get-Item $OutputExe).Length / 1MB, 1)
Write-Host ""
Write-Host "╔══════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║  BUILD COMPLETE                              ║" -ForegroundColor Green
Write-Host "║                                              ║" -ForegroundColor Green
Write-Host "║  Output : dist\Photobooth.Desktop.exe        ║" -ForegroundColor Green
Write-Host "║  Size   : $($SizeMb.ToString().PadRight(6)) MB                         ║" -ForegroundColor Green
Write-Host "╚══════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""
Write-Host "Run: .\dist\Photobooth.Desktop.exe" -ForegroundColor Yellow
