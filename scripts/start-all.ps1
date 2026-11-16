param(
    [string]$Mode = ""
)
$ErrorActionPreference = 'Stop'
$ProjectRoot = Resolve-Path "$PSScriptRoot\.."
Set-Location $ProjectRoot

function Invoke-RequiredCommand([string]$File, [string[]]$Arguments) {
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed: $File $($Arguments -join ' ')"
    }
}

function Find-PostgresTool([string]$Name) {
    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $candidate = Join-Path ${env:ProgramFiles} "PostgreSQL\18\bin\$Name.exe"
    if (Test-Path -LiteralPath $candidate) { return $candidate }
    return $null
}

function Reset-LocalData {
    $databaseTargets = @('ticketshield_db', 'identity_db', 'organizer_db', 'settlement_db')
    $localHost = if ($env:TICKETSHIELD_DB_HOST) { $env:TICKETSHIELD_DB_HOST } else { 'localhost' }
    $localPort = if ($env:TICKETSHIELD_DB_PORT) { $env:TICKETSHIELD_DB_PORT } else { '5432' }
    $localUser = if ($env:TICKETSHIELD_DB_USER) { $env:TICKETSHIELD_DB_USER } else { 'postgres' }
    $localPassword = if ($env:TICKETSHIELD_DB_PASSWORD) { $env:TICKETSHIELD_DB_PASSWORD } else { '12345' }

    Write-Host "`nResetting all local TicketShield data..." -ForegroundColor Yellow
    $dockerRunning = $false
    if (Get-Command docker -ErrorAction SilentlyContinue) {
        # docker info deliberately fails when Docker Desktop is stopped. Do not
        # let the script-wide Stop preference turn that probe into a failure.
        $previousErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            & docker info 2>$null | Out-Null
            $dockerRunning = $LASTEXITCODE -eq 0
        }
        finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }
    }
    if ($dockerRunning) {
        Invoke-RequiredCommand 'docker' @('compose', 'down', '--volumes', '--remove-orphans')
    }
    elseif ($choice -eq '1') {
        throw 'Docker Desktop is not running. Docker Compose mode requires Docker Desktop to be started first.'
    }

    # Docker PostgreSQL is stopped here. A reachable server is therefore a
    # separate native PostgreSQL instance and can be safely recreated too.
    $psql = Find-PostgresTool 'psql'
    $pgIsReady = Find-PostgresTool 'pg_isready'
    $nativeDatabaseReset = $false
    if ($psql -and $pgIsReady) {
        $env:PGPASSWORD = $localPassword
        try {
            & $pgIsReady '--host' $localHost '--port' $localPort '--username' $localUser | Out-Null
            if ($LASTEXITCODE -eq 0) {
                $nativeDatabaseReset = $true
                foreach ($database in $databaseTargets) {
                    Invoke-RequiredCommand $psql @('--set=ON_ERROR_STOP=1', '--no-psqlrc', '--host', $localHost, '--port', $localPort, '--username', $localUser, '--dbname', 'postgres', '--command', "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$database' AND pid <> pg_backend_pid();")
                    Invoke-RequiredCommand $psql @('--set=ON_ERROR_STOP=1', '--no-psqlrc', '--host', $localHost, '--port', $localPort, '--username', $localUser, '--dbname', 'postgres', '--command', "DROP DATABASE IF EXISTS $database;")
                    Invoke-RequiredCommand $psql @('--set=ON_ERROR_STOP=1', '--no-psqlrc', '--host', $localHost, '--port', $localPort, '--username', $localUser, '--dbname', 'postgres', '--command', "CREATE DATABASE $database;")
                }
            }
        }
        finally {
            Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
        }
    }

    if ($dockerRunning) {
        Invoke-RequiredCommand 'docker' @('compose', 'up', '-d', 'postgres', 'rabbitmq', 'redis')
    }
    elseif (-not $nativeDatabaseReset) {
        throw 'Docker Desktop is not running and no native PostgreSQL was found at localhost:5432. Start Docker Desktop, or start/configure local PostgreSQL first.'
    }
    elseif ($Mode -eq '2') {
        Write-Host 'Docker is off; using the cleaned native PostgreSQL instance. Redis/RabbitMQ use their development fallbacks when unavailable.' -ForegroundColor DarkYellow
    }
    Write-Host 'Local data reset completed.' -ForegroundColor Green
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "       TICKETSHIELD BACKEND MICROSERVICES LAUNCHER        " -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Select launch mode:" -ForegroundColor White
Write-Host "  [1] Docker Compose (Recommended - Starts DB, RabbitMQ & all APIs)" -ForegroundColor Green
Write-Host "  [2] .NET CLI (Starts MockOrganizer, Identity, Core API & Gateway in separate windows)" -ForegroundColor Cyan
Write-Host "  [3] Exit" -ForegroundColor Red
Write-Host ""

$choice = if (![string]::IsNullOrWhiteSpace($Mode)) { $Mode } else { Read-Host "Enter choice [1-3] (Default: 1)" }
if ([string]::IsNullOrWhiteSpace($choice)) { $choice = "1" }

function Start-NgrokTunnel {
    Write-Host "  -> Launching Ngrok Webhook Tunnel..." -ForegroundColor Magenta
    $ngrokBat = Join-Path $PSScriptRoot "run-ngrok.bat"
    if (Test-Path $ngrokBat) {
        Start-Process -FilePath "cmd.exe" -ArgumentList "/c", "`"$ngrokBat`""
    } else {
        Write-Host "[Warning] File run-ngrok.bat not found at $ngrokBat" -ForegroundColor Yellow
    }
}

switch ($choice) {
    "1" {
        Reset-LocalData
        $dopplerBin = if (Get-Command doppler -ErrorAction SilentlyContinue) { "doppler" }
                      elseif (Test-Path "$env:APPDATA\Python\Python313\Scripts\doppler.exe") { "$env:APPDATA\Python\Python313\Scripts\doppler.exe" }
                      else { "" }

        Write-Host "`nGenerating .env from Doppler secrets..." -ForegroundColor Cyan
        if ($dopplerBin) {
            & $dopplerBin secrets download --no-file --format env | Out-File -FilePath ".env" -Encoding utf8
        }
        if ($LASTEXITCODE -eq 0 -and $dopplerBin) {
            Write-Host "[OK] .env generated from Doppler (file is gitignored)." -ForegroundColor Green
        } else {
            Write-Host "[Warning] Doppler unavailable, proceeding without .env. SMTP may not work." -ForegroundColor Yellow
        }

        Write-Host "`nStarting services via Docker Compose..." -ForegroundColor Green
        docker compose up -d
        if ($LASTEXITCODE -ne 0) {
            Write-Host "`n[Warning] 'docker compose' failed. Trying 'docker-compose'..." -ForegroundColor Yellow
            docker-compose up -d
        }
        if ($LASTEXITCODE -eq 0) {
            Write-Host "`n[Success] Microservices containers started successfully!" -ForegroundColor Green
            docker ps --format "table {{.Names}}`t{{.Status}}`t{{.Ports}}"
            Start-NgrokTunnel
        } else {
            Write-Host "`n[Error] Could not start Docker Compose. Please check if Docker Desktop is running." -ForegroundColor Red
        }
    }
    "2" {
        Write-Host "`nStopping any existing microservice processes..." -ForegroundColor Yellow
        Stop-Process -Name "MockOrganizer.API", "TicketShield.Identity.API", "TicketShield.API", "TicketShield.Gateway", "TicketShield.AntiBot.Gateway", "TicketShield.Settlement.API", "dotnet", "ngrok" -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1
        Reset-LocalData

        Write-Host "`nBuilding solution first to prevent simultaneous compile file locks..." -ForegroundColor Cyan
        dotnet build TicketShield.sln
        if ($LASTEXITCODE -ne 0) {
            Write-Host "[Error] Build failed! Please fix compiler errors before running." -ForegroundColor Red
            return
        }

        Write-Host "`nLaunching .NET CLI microservices in separate windows..." -ForegroundColor Green

        $dopplerBin = if (Get-Command doppler -ErrorAction SilentlyContinue) { "doppler" }
                      elseif (Test-Path "$env:APPDATA\Python\Python313\Scripts\doppler.exe") { "$env:APPDATA\Python\Python313\Scripts\doppler.exe" }
                      else { "" }

        $services = @(
            @{ Name = "MockOrganizer.API";           Path = "src/MockOrganizer/MockOrganizer.API";                 UseDoppler = $true },
            @{ Name = "TicketShield.Identity.API";   Path = "src/TicketShield.Identity/TicketShield.Identity.API"; UseDoppler = $true },
            @{ Name = "TicketShield.API (Core)";     Path = "src/TicketShield.Core/TicketShield.API";             UseDoppler = $true },
            @{ Name = "TicketShield.Settlement.API"; Path = "src/TicketShield.Settlement.API";                 UseDoppler = $false },
            @{ Name = "TicketShield.Gateway";        Path = "src/TicketShield.Gateway";                          UseDoppler = $false },
            @{ Name = "TicketShield.AntiBot.Gateway";Path = "src/TicketShield.AntiBot.Gateway";                  UseDoppler = $false },
            @{ Name = "Frontend Web App (Vite)";    Path = (Join-Path $ProjectRoot "..\FE_CapstoneProject\apps\web"); IsNpm = $true }
        )

        foreach ($svc in $services) {
            Write-Host "  -> Launching $($svc.Name)..." -ForegroundColor Cyan
            $svcName = $svc.Name
            $svcPath = $svc.Path
            if ($svc.IsNpm) {
                $psCmd = "Set-Location '$svcPath'; Write-Host '=== $svcName ===' -ForegroundColor Yellow; npm run dev"
            } else {
                $runCmd = if ($svc.UseDoppler -and $dopplerBin) {
                    "& `"$dopplerBin`" run -- dotnet run --no-build --project `"$svcPath`""
                } else {
                    "dotnet run --no-build --project `"$svcPath`""
                }
                $psCmd = "Set-Location '$ProjectRoot'; Write-Host '=== $svcName ===' -ForegroundColor Yellow; $runCmd"
            }
            Start-Process powershell -ArgumentList "-NoExit", "-Command", $psCmd
        }

        Start-NgrokTunnel

        Write-Host "`n[Success] All microservices, Frontend Web App and Ngrok tunnel opened in separate windows!" -ForegroundColor Green
    }
    "3" {
        Write-Host "Exiting..." -ForegroundColor Gray
        exit
    }
    default {
        Write-Host "Invalid option." -ForegroundColor Red
    }
}

