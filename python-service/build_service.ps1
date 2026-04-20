<#
.SYNOPSIS
    Builds the Python FastAPI service into a standalone python_service.exe
    using PyInstaller, then copies the output to the WPF Resources folder.

.DESCRIPTION
    - Uses the .venv located at the repository root.
    - Installs PyInstaller if not already present.
    - Runs PyInstaller with the photobooth_service.spec file.
    - Copies the resulting exe to Photobooth.Desktop\Resources\.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ScriptDir   = $PSScriptRoot
$RepoRoot    = Split-Path $ScriptDir -Parent
$VenvPython  = Join-Path $RepoRoot ".venv\Scripts\python.exe"
$VenvPip     = Join-Path $RepoRoot ".venv\Scripts\pip.exe"
$SpecFile    = Join-Path $ScriptDir "photobooth_service.spec"
$DistDir     = Join-Path $ScriptDir "dist"
$ResourceDir = Join-Path $RepoRoot "Photobooth.Desktop\Resources"

Write-Host "=== Photobooth Python Service Build ===" -ForegroundColor Cyan

# Validate venv
if (-not (Test-Path $VenvPython)) {
    Write-Error "Virtual environment not found at '$VenvPython'. Run: python -m venv .venv"
}

# Install PyInstaller
Write-Host "[1/3] Installing PyInstaller into venv..." -ForegroundColor Yellow
& $VenvPip install --quiet pyinstaller

# Run PyInstaller
Write-Host "[2/3] Running PyInstaller..." -ForegroundColor Yellow
Push-Location $ScriptDir
try {
    & $VenvPython -m PyInstaller $SpecFile --distpath $DistDir --workpath "$ScriptDir\build" --noconfirm
    if ($LASTEXITCODE -ne 0) {
        throw "PyInstaller failed with exit code $LASTEXITCODE"
    }
} finally {
    Pop-Location
}

# Copy to WPF Resources folder
Write-Host "[3/3] Copying python_service.exe to WPF Resources..." -ForegroundColor Yellow
if (-not (Test-Path $ResourceDir)) {
    New-Item -ItemType Directory -Path $ResourceDir | Out-Null
}

$SourceExe = Join-Path $DistDir "python_service.exe"
if (-not (Test-Path $SourceExe)) {
    Write-Error "Build failed: '$SourceExe' not found."
}

Copy-Item -Path $SourceExe -Destination $ResourceDir -Force
Write-Host "Done: python_service.exe copied to $ResourceDir" -ForegroundColor Green
Write-Host "=== Build complete ===" -ForegroundColor Cyan
