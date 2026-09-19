################################################################################
##  File:  Install-DotnetSDK.ps1
##  Desc:  Install specified versions of the .NET SDK
################################################################################

param (
    [string[]] $SdkVersions
)

# Default SDK versions to install
$DefaultSdkVersions = @(
    "8.0.413",
    "10.0.101"
)

if (-not $SdkVersions) {
    $SdkVersions = $DefaultSdkVersions
}

# Set environment variables
[Environment]::SetEnvironmentVariable("DOTNET_MULTILEVEL_LOOKUP", "0", "Machine")
[Environment]::SetEnvironmentVariable("DOTNET_NOLOGO", "1", "Machine")
[Environment]::SetEnvironmentVariable("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1", "Machine")

[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor "Tls12"

$ErrorActionPreference = "Stop"
$ProgressPreference = 'SilentlyContinue'

foreach ($sdkVersion in $SdkVersions) {
    if (Test-Path -Path "C:\Program Files\dotnet\sdk\$sdkVersion") {
        Write-Host ".NET SDK $sdkVersion already installed, skipping"
        continue
    }

    $installerUrl = "https://builds.dotnet.microsoft.com/dotnet/Sdk/${sdkVersion}/dotnet-sdk-${sdkVersion}-win-x64.exe"
    $installerPath = Join-Path $env:TEMP "dotnet-sdk-${sdkVersion}-installer.exe"

    Write-Host "Downloading .NET SDK $sdkVersion..."
    Invoke-WebRequest -Uri $installerUrl -OutFile $installerPath

    Write-Host "Installing .NET SDK $sdkVersion..."
    $process = Start-Process -FilePath $installerPath -ArgumentList '/quiet', '/norestart' -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw ".NET SDK $sdkVersion installer failed with exit code $($process.ExitCode)"
    }
    Remove-Item $installerPath -Force

    # Clean workload manifests to reduce image size
    Remove-Item -Recurse -Force "C:\Program Files\dotnet\sdk-manifests\*" -ErrorAction SilentlyContinue

    Write-Host ".NET SDK $sdkVersion installed successfully"
}

# Add dotnet to PATH
$dotnetPath = "C:\Program Files\dotnet"
$machinePath = [Environment]::GetEnvironmentVariable("PATH", "Machine")
if ($machinePath -notlike "*$dotnetPath*") {
    [Environment]::SetEnvironmentVariable("PATH", "$machinePath;$dotnetPath", "Machine")
    Write-Host "Added $dotnetPath to system PATH"
}
$env:PATH = "$env:PATH;$dotnetPath"
echo "$dotnetPath" >> $env:GITHUB_PATH

Write-Host "All .NET SDK installations complete"
