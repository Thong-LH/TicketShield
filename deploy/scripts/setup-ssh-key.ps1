[CmdletBinding()]
param(
    [string]$VpsHost = '103.183.120.83',
    [string]$SshUser = 'root',
    [string]$KeyPath = "$env:USERPROFILE\.ssh\ticketshield_vps_ed25519"
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command ssh -ErrorAction SilentlyContinue)) {
    throw 'OpenSSH client is not installed on Windows.'
}

if (-not (Test-Path -LiteralPath $KeyPath)) {
    $keyDirectory = Split-Path -Parent $KeyPath
    New-Item -ItemType Directory -Force -Path $keyDirectory | Out-Null
    & ssh-keygen -t ed25519 -f $KeyPath -N '""' -C 'ticketshield-vps-deploy'
    if ($LASTEXITCODE -ne 0) {
        throw 'ssh-keygen failed.'
    }
}

$publicKeyPath = "${KeyPath}.pub"
if (-not (Test-Path -LiteralPath $publicKeyPath)) {
    throw "Public key not found: $publicKeyPath"
}

$publicKey = (Get-Content -LiteralPath $publicKeyPath -Raw).Trim()
$escapedPublicKey = $publicKey.Replace("'", "'\"'\"'")
$remoteCommand = "umask 077; mkdir -p ~/.ssh; touch ~/.ssh/authorized_keys; grep -qxF '$escapedPublicKey' ~/.ssh/authorized_keys || printf '%s\n' '$escapedPublicKey' >> ~/.ssh/authorized_keys; chmod 700 ~/.ssh; chmod 600 ~/.ssh/authorized_keys"

Write-Host 'Enter the current VPS password once to install the SSH public key.' -ForegroundColor Cyan
& ssh "${SshUser}@${VpsHost}" $remoteCommand
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to install the SSH public key.'
}

& ssh -o BatchMode=yes -i $KeyPath "${SshUser}@${VpsHost}" 'printf SSH_KEY_OK'
if ($LASTEXITCODE -ne 0) {
    throw 'SSH key verification failed.'
}

Write-Host "SSH key authentication is ready: $KeyPath" -ForegroundColor Green

