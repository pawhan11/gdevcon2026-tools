################################################################################
##  File:  Install-NIPackageManager.ps1
##  Desc:  Install NI Package Manager
################################################################################

param (
    [string] $Version = "26.0.0"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$installerFileName = "NIPackageManager${Version}.exe"
$downloadUrl = "https://download.ni.com/support/nipkg/products/ni-package-manager/installers/${installerFileName}"
$installerPath = Join-Path $env:TEMP $installerFileName

Write-Host "Downloading NI Package Manager $Version..."
Invoke-WebRequest -Uri $downloadUrl -OutFile $installerPath

Write-Host "Installing NI Package Manager $Version..."
$process = Start-Process -FilePath $installerPath -ArgumentList "--passive", "--accept-eulas", "--prevent-reboot" -Wait -PassThru
if ($process.ExitCode -ne 0) {
    throw "NI Package Manager installer failed with exit code $($process.ExitCode)"
}
Remove-Item $installerPath -Force

# Add NI Package Manager to PATH
$nipkgPath = "C:\Program Files\National Instruments\NI Package Manager"
$machinePath = [Environment]::GetEnvironmentVariable("PATH", "Machine")
if ($machinePath -notlike "*$nipkgPath*") {
    [Environment]::SetEnvironmentVariable("PATH", "$machinePath;$nipkgPath", "Machine")
}
$env:PATH = "$env:PATH;$nipkgPath"
echo "$nipkgPath" >> $env:GITHUB_PATH

Write-Host "Verifying NI Package Manager..."
& "$nipkgPath\nipkg.exe" --version
if ($LASTEXITCODE -ne 0) {
    throw "NI Package Manager verification failed with exit code $LASTEXITCODE"
}

Write-Host "NI Package Manager $Version installed successfully"
