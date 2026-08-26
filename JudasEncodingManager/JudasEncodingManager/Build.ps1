<# 
    Judas Encoding Manager - Build Script
    Creates a single-file executable for distribution
#>

param(
    [switch]$Clean,
    [switch]$Release,
    [string]$ExpectedVersion
)

$ErrorActionPreference = "Stop"

function ConvertTo-ReleaseVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Version,
        [Parameter(Mandatory = $true)]
        [string]$Source
    )

    $match = [regex]::Match(
        $Version.Trim(),
        '^[vV]?(?<major>\d+)\.(?<minor>\d+)(?:\.(?<patch>\d+))?(?:\.\d+)?$'
    )
    if (-not $match.Success) {
        throw "Invalid $Source version '$Version'. Expected a version such as 1.3.1 or v1.3.1."
    }

    $patch = if ($match.Groups["patch"].Success) {
        $match.Groups["patch"].Value
    } else {
        "0"
    }

    return "$($match.Groups["major"].Value).$($match.Groups["minor"].Value).$patch"
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Judas Encoding Manager Build Script  " -ForegroundColor Cyan  
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Get script directory
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptDir

$projectPath = Join-Path $ScriptDir "JudasEncodingManager.csproj"

if ($ExpectedVersion) {
    Write-Host "[release] Validating release version..." -ForegroundColor Yellow

    $projectVersionNode = Select-Xml -Path $projectPath -XPath "/*[local-name()='Project']/*[local-name()='PropertyGroup']/*[local-name()='Version']" |
        Select-Object -First 1
    if (-not $projectVersionNode) {
        throw "Release version mismatch: no <Version> was found in '$projectPath'."
    }

    $tagVersion = ConvertTo-ReleaseVersion -Version $ExpectedVersion -Source "Git tag"
    $projectVersion = ConvertTo-ReleaseVersion -Version $projectVersionNode.Node.InnerText -Source "project"
    if ($tagVersion -ne $projectVersion) {
        throw "Release version mismatch: Git tag '$ExpectedVersion' resolves to '$tagVersion', but the project version is '$projectVersion'."
    }

    Write-Host "      Tag and project version: $tagVersion" -ForegroundColor Green
}

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
dotnet run --project "..\RegressionTests\JudasEncodingManager.RegressionTests.csproj" --framework net8.0-windows
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

if ($ExpectedVersion) {
    $exeVersionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath)
    $exeVersion = ConvertTo-ReleaseVersion -Version $exeVersionInfo.FileVersion -Source "executable"
    $tagVersion = ConvertTo-ReleaseVersion -Version $ExpectedVersion -Source "Git tag"
    if ($tagVersion -ne $exeVersion) {
        throw "Release version mismatch: Git tag '$ExpectedVersion' resolves to '$tagVersion', but the packaged executable version is '$exeVersion'."
    }

    Write-Host "      Packaged executable version: $exeVersion" -ForegroundColor Green
}

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
