# TicketShield Microservices Start Script
$ProjectRoot = Resolve-Path "$PSScriptRoot\.."
Set-Location $ProjectRoot

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "       TICKETSHIELD BACKEND MICROSERVICES LAUNCHER        " -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Select launch mode:" -ForegroundColor White
Write-Host "  [1] Docker Compose (Recommended - Starts DB, RabbitMQ & all APIs)" -ForegroundColor Green
Write-Host "  [2] .NET CLI (Starts MockOrganizer, Identity, Core API & Gateway in separate windows)" -ForegroundColor Cyan
Write-Host "  [3] Exit" -ForegroundColor Red
Write-Host ""

$choice = Read-Host "Enter choice [1-3] (Default: 1)"
if ([string]::IsNullOrWhiteSpace($choice)) { $choice = "1" }

switch ($choice) {
    "1" {
        Write-Host "`nGenerating .env from Doppler secrets..." -ForegroundColor Cyan
        doppler secrets download --no-file --format env | Out-File -FilePath ".env" -Encoding utf8
        if ($LASTEXITCODE -eq 0) {
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
        } else {
            Write-Host "`n[Error] Could not start Docker Compose. Please check if Docker Desktop is running." -ForegroundColor Red
        }
    }
    "2" {
        Write-Host "`nStopping any existing microservice processes..." -ForegroundColor Yellow
        Stop-Process -Name "MockOrganizer.API", "TicketShield.Identity.API", "TicketShield.API", "TicketShield.Gateway" -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1

        Write-Host "`nLaunching .NET CLI microservices in separate windows..." -ForegroundColor Green

        $services = @(
            @{ Name = "MockOrganizer.API";         Path = "src/MockOrganizer/MockOrganizer.API";                 UseDoppler = $true },
            @{ Name = "TicketShield.Identity.API"; Path = "src/TicketShield.Identity/TicketShield.Identity.API"; UseDoppler = $true },
            @{ Name = "TicketShield.API (Core)";   Path = "src/TicketShield.Core/TicketShield.API";             UseDoppler = $true },
            @{ Name = "TicketShield.Gateway";      Path = "src/TicketShield.Gateway";                          UseDoppler = $false }
        )

        foreach ($svc in $services) {
            Write-Host "  -> Launching $($svc.Name)..." -ForegroundColor Cyan
            $svcName = $svc.Name
            $svcPath = $svc.Path
            $runCmd = if ($svc.UseDoppler) {
                "doppler run -- dotnet run --project `"$svcPath`""
            } else {
                "dotnet run --project `"$svcPath`""
            }
            $psCmd = "Set-Location '$ProjectRoot'; Write-Host '=== $svcName ===' -ForegroundColor Yellow; $runCmd"
            Start-Process powershell -ArgumentList "-NoExit", "-Command", $psCmd
        }
        Write-Host "`n[Success] All 4 microservice windows opened!" -ForegroundColor Green
    }
    "3" {
        Write-Host "Exiting..." -ForegroundColor Gray
        exit
    }
    default {
        Write-Host "Invalid option." -ForegroundColor Red
    }
}
