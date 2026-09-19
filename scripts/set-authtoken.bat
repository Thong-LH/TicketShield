@echo off
title Setup Ngrok Authtoken - TicketShield
cd /d "%~dp0"
echo ==========================================================
echo        TICKETSHIELD NGROK AUTHTOKEN CONFIGURATION
echo ==========================================================
echo.
echo 1. Neu ban chua co Authtoken, hay dang ky/dang nhap tai:
echo    https://dashboard.ngrok.com/get-started/your-authtoken
echo.
set /p TOKEN="Nhap ma Authtoken cua ban roi nhan Enter: "

if "%TOKEN%"=="" (
    echo [ERROR] Ban chua nhap authtoken!
    pause
    exit /b 1
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
