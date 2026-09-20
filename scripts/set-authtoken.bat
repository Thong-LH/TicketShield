@echo off
title Setup Ngrok Authtoken - TicketShield
cd /d "%~dp0"
echo ==========================================================
echo        TICKETSHIELD NGROK AUTHTOKEN CONFIGURATION
echo ==========================================================
echo.
echo Domain co dinh cua team: https://chest-huddling-asleep.ngrok-free.dev
echo Authtoken chinh chủ cua team: 3JXNY7QYKZqBhfWaoBKuWnIqGCz_6AuVRJZ4PtYmznzR8tWi2
echo.
set "DEFAULT_TOKEN=3JXNY7QYKZqBhfWaoBKuWnIqGCz_6AuVRJZ4PtYmznzR8tWi2"
set /p TOKEN="Nhap Authtoken (Nhan Enter de dung Authtoken dung chung cua team): "

if "%TOKEN%"=="" (
    set "TOKEN=%DEFAULT_TOKEN%"
)

"%~dp0ngrok.exe" config add-authtoken %TOKEN%
if %errorlevel% equ 0 (
    echo.
    echo [SUCCESS] Luu authtoken thanh cong!
    echo Bay gio ban co the chay scripts\run-ngrok.bat de bat dau Webhook tunnel.
) else (
    echo.
    echo [ERROR] Khong the luu authtoken. Vui long kiem tra lai ma token.
)
echo.
pause

