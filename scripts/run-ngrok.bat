@echo off
title TicketShield Ngrok Webhook Tunnel
cd /d "%~dp0"
echo ==========================================================
echo        TICKETSHIELD NGROK WEBHOOK TUNNEL LAUNCHER
echo ==========================================================
echo.

set NGROK_CMD=
set NGROK_EXE="%~dp0ngrok.exe"

:: 1. Kiem tra file ngrok.exe trong thu muc scripts
if exist %NGROK_EXE% (
    set NGROK_CMD=%NGROK_EXE%
    goto FOUND_NGROK
)

:: 2. Kiem tra lenh ngrok toan cuc (Global PATH)
where ngrok >nul 2>&1
if %ERRORLEVEL% equ 0 (
    set NGROK_CMD=ngrok
    goto FOUND_NGROK
)

:: 3. Neu chua co, tu dong tai ngrok.exe ve thu muc scripts
echo [!] May ban chua co ngrok.exe. Dang tu dong tai xuong ve thu muc scripts...
powershell -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri 'https://bin.equinox.io/c/bNyj1mQVY4c/ngrok-v3-stable-windows-amd64.zip' -OutFile '%~dp0ngrok.zip'; Expand-Archive -Path '%~dp0ngrok.zip' -DestinationPath '%~dp0' -Force; Remove-Item '%~dp0ngrok.zip' -Force"

if exist %NGROK_EXE% (
    echo [+] Tai ngrok thanh cong!
    set NGROK_CMD=%NGROK_EXE%
    goto FOUND_NGROK
) else (
    echo [x] Khong the tu dong tai ngrok. Vui long cai dat qua lenh: winget install ngrok
    pause
    exit /b 1
)

:FOUND_NGROK
echo.
echo [+] Dang thiet lap Authtoken & mo cong tunnel co dinh cho Gateway (5000)...
echo [+] Domain co dinh: https://chest-huddling-asleep.ngrok-free.dev
echo.

:: Thiet lap authtoken dung chung cua team
%NGROK_CMD% config add-authtoken 3JXNY7QYKZqBhfWaoBKuWnIqGCz_6AuVRJZ4PtYmznzR8tWi2 >nul 2>&1


:: Khoi chay tunnel toi Gateway port 5000 voi URL co dinh
%NGROK_CMD% http 5000 --url=https://chest-huddling-asleep.ngrok-free.dev

pause
