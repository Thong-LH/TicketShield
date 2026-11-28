[CmdletBinding()]
param(
    [ValidateSet('Bootstrap', 'Deploy', 'ResetDatabase', 'Status', 'Logs')]
    [string]$Action = 'Deploy',
    [string]$VpsHost = '103.183.120.83',
    [string]$SshUser = 'root',
    [string]$KeyPath = "$env:USERPROFILE\.ssh\ticketshield_vps_ed25519",
    [switch]$ConfirmReset
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $KeyPath)) {
    throw "SSH key not found. Run deploy/scripts/setup-ssh-key.ps1 first: $KeyPath"
}

$sshTarget = "${SshUser}@${VpsHost}"
$sshArgs = @('-t', '-i', $KeyPath, $sshTarget)

switch ($Action) {
    'Bootstrap' {
        $bootstrapUrl = 'https://raw.githubusercontent.com/Thong-LH/TicketShield/refs/heads/deploy/production-vps/deploy/scripts/bootstrap-vps.sh'
        $remoteCommand = "set -Eeuo pipefail; tmp_file=`$(mktemp); trap 'rm -f `"`$tmp_file`"' EXIT; curl -fsSL '$bootstrapUrl' -o `"`$tmp_file`"; bash `"`$tmp_file`""
    }
    'Deploy' {
        $remoteCommand = "sudo -u ticketshield-deploy -H bash /opt/ticketshield/repo/deploy/scripts/deploy-vps.sh"
    }
    'ResetDatabase' {
        if (-not $ConfirmReset) {
            throw 'Database reset requires the explicit -ConfirmReset switch.'
        }
        $remoteCommand = "sudo -u ticketshield-deploy -H bash /opt/ticketshield/repo/deploy/scripts/reset-database.sh --confirm=RESET_TICKETSHIELD_DATABASE"
    }
    'Status' {
        $remoteCommand = "sudo -u ticketshield-deploy -H bash -lc 'source /etc/ticketshield/deploy.conf; export DOPPLER_TOKEN=`$(cat `"`$DOPPLER_TOKEN_FILE`"); cd `"`$REPO_DIR`"; doppler run -p `"`$DOPPLER_PROJECT`" -c `"`$DOPPLER_CONFIG`" -- docker compose -f deploy/compose.production.yaml ps'"
    }
    'Logs' {
        $remoteCommand = "sudo -u ticketshield-deploy -H bash -lc 'source /etc/ticketshield/deploy.conf; export DOPPLER_TOKEN=`$(cat `"`$DOPPLER_TOKEN_FILE`"); cd `"`$REPO_DIR`"; doppler run -p `"`$DOPPLER_PROJECT`" -c `"`$DOPPLER_CONFIG`" -- docker compose -f deploy/compose.production.yaml logs --tail=200'"
    }
}

& ssh @sshArgs $remoteCommand
if ($LASTEXITCODE -ne 0) {
    throw "VPS action failed: $Action"
}

