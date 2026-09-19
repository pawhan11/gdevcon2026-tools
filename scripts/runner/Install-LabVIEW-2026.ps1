################################################################################
##  File:  Install-LabVIEW-2026.ps1
##  Desc:  Install NI LabVIEW 2026 and build vipm2 CLI
##         Requires NI Package Manager installed
################################################################################

$ErrorActionPreference = "Continue"

$packagesDir = "C:\ProgramData\National Instruments\NI Package Manager\packages"
$productCacheDir = "C:\ProgramData\National Instruments\MDF\ProductCache"

# Configure NI package feeds and update
Write-Host "Configuring NI package feeds for LabVIEW 2026..."
$feeds = @{
    "ni-labview-2026"          = "https://download.ni.com/support/nipkg/products/ni-l/ni-labview-2026/26.1/released"
    "ni-labview-2026-database" = "https://download.ni.com/support/nipkg/products/ni-l/ni-labview-database-toolkit/26.0/released"
    "ni-labview-2026-rgt"      = "https://download.ni.com/support/nipkg/products/ni-l/ni-labview-report-generation-toolkit/26.0/released"
}

foreach ($feed in $feeds.GetEnumerator()) {
    Write-Host "Adding feed $($feed.Key)..."
    nipkg feed-add --name=$($feed.Key) $feed.Value
}

nipkg update

# Install LabVIEW packages
$packages = @(
    "ni-labview-2026-core-en",
    "ni-labview-command-line-interface-x86",
    "ni-dct-labview-support",
    "ni-rgt-labview-support"
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

# Configure LabVIEW.ini - enable VI Server for container/CLI use
$labviewIniPath = "C:\Program Files\National Instruments\LabVIEW 2026\LabVIEW.ini"
Write-Host "Writing LabVIEW.ini to $labviewIniPath..."
Set-Content -Path $labviewIniPath -Value @"
[LabVIEW]
server.tcp.enabled=True
server.tcp.port=3363
server.vi.access=+*
"@ -Encoding ASCII

# Configure LabVIEWCLI.ini
$labviewCliIniPath = "C:\Program Files (x86)\National Instruments\Shared\LabVIEW CLI\LabVIEWCLI.ini"
Write-Host "Writing LabVIEWCLI.ini to $labviewCliIniPath..."
Set-Content -Path $labviewCliIniPath -Value @"
[LabVIEWCLI]
UDCInstallID = "5da7e86a-5ddf-4154-b231-0d1fba62600d"
DefaultPortNumber = 3363
AppendToLogFile = FALSE
AppExitTimeout = 10000
DefaultLabVIEWPath = ""
OpenAppReferenceTimeoutInSecond = 3.000000
AfterLaunchOpenAppReferenceTimeoutInSecond = 60.000000
DeleteLabVIEWCLILogFile = FALSE
PingDelay = -1
PingTimeout = 10000
IndividualOpenAppRefTimeout = 20
ValidateLabVIEWLicense = TRUE
"@ -Encoding ASCII

# Build VIPM2
$repoRoot = (Get-Item $PSScriptRoot).Parent.Parent.FullName
$csprojPath = Join-Path $repoRoot "vipm2\vipm2.csproj"
$outputPath = "C:\Program Files\Convertertec\vipm2"

Write-Host "Building vipm2..."
dotnet build $csprojPath --configuration Release --output $outputPath
if ($LASTEXITCODE -ne 0) { throw "dotnet build vipm2 failed with exit code $LASTEXITCODE" }

# Add vipm2 to PATH
$machinePath = [Environment]::GetEnvironmentVariable("PATH", "Machine")
if ($machinePath -notlike "*$outputPath*") {
    [Environment]::SetEnvironmentVariable("PATH", "$machinePath;$outputPath", "Machine")
}
$env:PATH = "$env:PATH;$outputPath"
echo "$outputPath" >> $env:GITHUB_PATH

# Verify vipm2 is callable
Write-Host "Verifying vipm2..."
vipm2 --version
if ($LASTEXITCODE -ne 0) { throw "vipm2 verification failed with exit code $LASTEXITCODE" }

Write-Host "LabVIEW 2026 and vipm2 installed successfully"
