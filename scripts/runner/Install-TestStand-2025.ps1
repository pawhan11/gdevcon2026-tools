################################################################################
##  File:  Install-TestStand-2025.ps1
##  Desc:  Install NI TestStand 2025 and activate license via TestStand CLI
##         Requires NI Package Manager installed
################################################################################

param (
    [Parameter(Mandatory)]
    [string] $SerialNumber
)

$ErrorActionPreference = "Continue"

$packagesDir = "C:\ProgramData\National Instruments\NI Package Manager\packages"
$productCacheDir = "C:\ProgramData\National Instruments\MDF\ProductCache"

# Configure NI package feeds and update
Write-Host "Configuring NI package feeds for TestStand 2025..."
$feeds = @{
    "ni-teststand-2025" = "https://download.ni.com/support/nipkg/products/ni-t/ni-teststand-2025/25.5/released"
}

foreach ($feed in $feeds.GetEnumerator()) {
    Write-Host "Adding feed $($feed.Key)..."
    nipkg feed-add --name=$($feed.Key) $feed.Value
}

nipkg update

# Install TestStand packages
$packages = @(
    "ni-teststand-2025",
    "ni-teststand-2025-tools",
    "ni-teststand-2025-process-models-source",
    "ni-teststand-2025-process-models-runtime"
)

foreach ($package in $packages) {
    Write-Host "Installing $package..."
    nipkg install --yes --accept-eulas --assume-yes $package
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "$package install returned exit code $LASTEXITCODE"
    }

    Remove-Item -Path "$packagesDir\*.nipkg" -Force -ErrorAction SilentlyContinue
    Remove-Item -Path $productCacheDir -Recurse -Force -ErrorAction SilentlyContinue
}

# Build TestStand CLI
$repoRoot = (Get-Item $PSScriptRoot).Parent.Parent.FullName
$csprojPath = Join-Path $repoRoot "TeststandCLI\TeststandCLI.csproj"
$outputPath = "C:\Program Files\Convertertec\TestStandCLI"

Write-Host "Building TestStand CLI..."
dotnet build $csprojPath --configuration Release --output $outputPath
if ($LASTEXITCODE -ne 0) { throw "dotnet build TestStandCLI failed with exit code $LASTEXITCODE" }

# Add TestStandCLI to PATH
$machinePath = [Environment]::GetEnvironmentVariable("PATH", "Machine")
if ($machinePath -notlike "*$outputPath*") {
    [Environment]::SetEnvironmentVariable("PATH", "$machinePath;$outputPath", "Machine")
}
$env:PATH = "$env:PATH;$outputPath"
echo "$outputPath" >> $env:GITHUB_PATH

# Enable and activate NI TestStand
Write-Host "Configuring NI TestStand 2025..."
TestStandCLI list-installed -v
if ($LASTEXITCODE -ne 0) { throw "TestStandCLI list-installed failed with exit code $LASTEXITCODE" }

TestStandCLI set-active 25.5 -v
if ($LASTEXITCODE -ne 0) { throw "TestStandCLI set-active failed with exit code $LASTEXITCODE" }

TestStandCLI check-active -v
if ($LASTEXITCODE -ne 0) { throw "TestStandCLI check-active failed with exit code $LASTEXITCODE" }

TestStandCLI activate-license $SerialNumber -v
if ($LASTEXITCODE -ne 0) { throw "TestStandCLI activate-license failed with exit code $LASTEXITCODE" }

TestStandCLI check-license -v
if ($LASTEXITCODE -ne 0) { throw "TestStandCLI check-license failed with exit code $LASTEXITCODE" }

Write-Host "NI TestStand 2025 installed and activated successfully"
