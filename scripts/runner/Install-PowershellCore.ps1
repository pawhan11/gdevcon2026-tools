################################################################################
##  File:  Install-PowershellCore.ps1
##  Desc:  Install PowerShell Core (LTS release)
################################################################################

param (
    [string] $MajorMinorVersion = "7.4"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor "Tls12"

# Get PowerShell release metadata to find the matching LTS version
Write-Host "Finding PowerShell Core LTS release for version $MajorMinorVersion..."
$metadata = Invoke-RestMethod https://raw.githubusercontent.com/PowerShell/PowerShell/master/tools/metadata.json

$targetRelease = $null
foreach ($release in ($metadata.LTSReleaseTag -replace '^v')) {
    if ($release -like "${MajorMinorVersion}*") {
        $targetRelease = $release
        break
    }
}

if (-not $targetRelease) {
    throw "Could not find LTS release matching version $MajorMinorVersion"
}

$downloadUrl = "https://github.com/PowerShell/PowerShell/releases/download/v${targetRelease}/PowerShell-${targetRelease}-win-x64.msi"
$installerPath = Join-Path $env:TEMP "PowerShell-${targetRelease}-win-x64.msi"

Write-Host "Downloading PowerShell Core $targetRelease..."
Invoke-WebRequest -Uri $downloadUrl -OutFile $installerPath

Write-Host "Installing PowerShell Core $targetRelease..."
$process = Start-Process msiexec.exe -ArgumentList "/i", $installerPath, "/quiet", "/norestart" -Wait -PassThru
if ($process.ExitCode -ne 0) {
    throw "PowerShell Core installer failed with exit code $($process.ExitCode)"
}
Remove-Item $installerPath -Force

# Disable update notifications
[Environment]::SetEnvironmentVariable("POWERSHELL_UPDATECHECK", "Off", "Machine")

Write-Host "PowerShell Core $targetRelease installed successfully"
