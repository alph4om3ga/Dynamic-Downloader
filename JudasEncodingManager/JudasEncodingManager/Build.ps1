<# 
    Judas Encoding Manager - Build Script
    Creates a single-file executable for distribution
#>

param(
    [switch]$Clean,
    [switch]$Release
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Judas Encoding Manager Build Script  " -ForegroundColor Cyan  
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Get script directory
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptDir

# Clean if requested
if ($Clean) {
    Write-Host "[1/5] Cleaning previous builds..." -ForegroundColor Yellow
    if (Test-Path "bin") { Remove-Item -Recurse -Force "bin" }
    if (Test-Path "obj") { Remove-Item -Recurse -Force "obj" }
    Write-Host "      Cleaned!" -ForegroundColor Green
} else {
    Write-Host "[1/5] Skipping clean (use -Clean to clean)" -ForegroundColor Gray
}

# Restore packages
Write-Host "[2/5] Restoring NuGet packages..." -ForegroundColor Yellow
dotnet restore
if ($LASTEXITCODE -ne 0) { throw "Restore failed" }
Write-Host "      Restored!" -ForegroundColor Green

# Run focused safety checks before producing a release build.
Write-Host "[3/5] Running regression checks..." -ForegroundColor Yellow
dotnet run --project "..\RegressionTests\JudasEncodingManager.RegressionTests.csproj"
if ($LASTEXITCODE -ne 0) { throw "Regression checks failed" }
Write-Host "      Passed!" -ForegroundColor Green

# Build
Write-Host "[4/5] Building project..." -ForegroundColor Yellow
dotnet build -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Write-Host "      Built!" -ForegroundColor Green

# Publish single-file executable
Write-Host "[5/5] Publishing single-file executable..." -ForegroundColor Yellow
dotnet publish -c Release --no-build -o ".\publish"
if ($LASTEXITCODE -ne 0) { throw "Publish failed" }
Write-Host "      Published!" -ForegroundColor Green

# Summary
$exePath = Join-Path $ScriptDir "publish\JudasEncodingManager.exe"
$exeSize = [math]::Round((Get-Item $exePath).Length / 1MB, 2)

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Build Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Executable: $exePath" -ForegroundColor White
Write-Host "  Size: $exeSize MB" -ForegroundColor White
Write-Host ""
Write-Host "  You can now:" -ForegroundColor Cyan
Write-Host "  1. Run the executable directly" -ForegroundColor White
Write-Host "  2. Create a shortcut to the executable" -ForegroundColor White
Write-Host "  3. Copy to any Windows x64 machine (no .NET required)" -ForegroundColor White
Write-Host ""

# Open publish folder
if ($Release) {
    explorer.exe ".\publish"
}
