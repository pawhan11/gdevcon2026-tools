################################################################################
##  File:  Install-CycloneDxCli.ps1
##  Desc:  Install the CycloneDX CLI (cyclonedx.exe) for merging/converting SBOMs
##         Not available as a dotnet tool, so the pinned release binary is downloaded
################################################################################

param (
    [string] $Version = "v0.33.1"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor "Tls12"

$installDir = "C:\Program Files\Convertertec\cyclonedx-cli"
$exePath = Join-Path $installDir "cyclonedx.exe"
$downloadUrl = "https://github.com/CycloneDX/cyclonedx-cli/releases/download/$Version/cyclonedx-win-x64.exe"

New-Item -Path $installDir -ItemType Directory -Force | Out-Null

Write-Host "Downloading CycloneDX CLI $Version from $downloadUrl..."
Invoke-WebRequest -Uri $downloadUrl -OutFile $exePath -UseBasicParsing

# Add cyclonedx to PATH
$machinePath = [Environment]::GetEnvironmentVariable("PATH", "Machine")
if ($machinePath -notlike "*$installDir*") {
    [Environment]::SetEnvironmentVariable("PATH", "$machinePath;$installDir", "Machine")
}
$env:PATH = "$env:PATH;$installDir"
echo "$installDir" >> $env:GITHUB_PATH

# Verify cyclonedx is callable
Write-Host "Verifying cyclonedx..."
cyclonedx --version
if ($LASTEXITCODE -ne 0) { throw "cyclonedx verification failed with exit code $LASTEXITCODE" }

Write-Host "CycloneDX CLI $Version installed successfully"
