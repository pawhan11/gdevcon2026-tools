################################################################################
##  File:  Install-Git.ps1
##  Desc:  Install Git for Windows (latest release)
################################################################################

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor "Tls12"

# Find latest Git for Windows release
Write-Host "Finding latest Git for Windows release..."
$releasesApi = "https://api.github.com/repos/git-for-windows/git/releases/latest"
$release = Invoke-RestMethod -Uri $releasesApi -Headers @{ "User-Agent" = "PowerShell" }
$asset = $release.assets | Where-Object { $_.name -like "Git-*-64-bit.exe" } | Select-Object -First 1

if (-not $asset) {
    throw "Could not find Git 64-bit installer in latest release"
}

$downloadUrl = $asset.browser_download_url
$installerPath = Join-Path $env:TEMP $asset.name

Write-Host "Downloading $($asset.name)..."
Invoke-WebRequest -Uri $downloadUrl -OutFile $installerPath

Write-Host "Installing Git for Windows..."
$process = Start-Process -FilePath $installerPath -ArgumentList @(
    "/VERYSILENT",
    "/NORESTART",
    "/NOCANCEL",
    "/SP-",
    "/CLOSEAPPLICATIONS",
    "/RESTARTAPPLICATIONS",
    "/o:PathOption=CmdTools",
    "/o:BashTerminalOption=ConHost",
    "/o:EnableSymlinks=Enabled",
    "/COMPONENTS=gitlfs"
) -Wait -PassThru
if ($process.ExitCode -ne 0) {
    throw "Git installer failed with exit code $($process.ExitCode)"
}

Remove-Item $installerPath -Force

# Add Git to PATH
$gitPath = "C:\Program Files\Git\bin"
$machinePath = [Environment]::GetEnvironmentVariable("PATH", "Machine")
if ($machinePath -notlike "*$gitPath*") {
    [Environment]::SetEnvironmentVariable("PATH", "$machinePath;$gitPath", "Machine")
}
$env:PATH = "$env:PATH;$gitPath"
echo "$gitPath" >> $env:GITHUB_PATH

# Configure git
& "C:\Program Files\Git\bin\git.exe" config --system --add safe.directory "*"
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to configure safe.directory for Git with exit code $LASTEXITCODE"
}

# Disable GCM machine-wide
[Environment]::SetEnvironmentVariable("GCM_INTERACTIVE", "Never", "Machine")

# Add well-known SSH host keys to ssh_known_hosts
$sshKeyScan = "C:\Program Files\Git\usr\bin\ssh-keyscan.exe"
if (Test-Path $sshKeyScan) {
    Write-Host "Adding SSH known hosts..."
    $windowsSSHKnownHosts = "C:\ProgramData\ssh\ssh_known_hosts"
    $gitSSHKnownHosts = "C:\Program Files\Git\etc\ssh\ssh_known_hosts"

    New-Item -Path (Split-Path $windowsSSHKnownHosts) -ItemType Directory -Force -ErrorAction SilentlyContinue | Out-Null

    $githubHostKeys = & $sshKeyScan -t rsa,ecdsa,ed25519 github.com 2>$null
    $azureHostKeys = & $sshKeyScan -t rsa ssh.dev.azure.com 2>$null

    Set-Content -Encoding ASCII -Path $windowsSSHKnownHosts, $gitSSHKnownHosts -Value $githubHostKeys
    Add-Content -Encoding ASCII -Path $windowsSSHKnownHosts, $gitSSHKnownHosts -Value $azureHostKeys
}

Write-Host "Git for Windows installed successfully"
