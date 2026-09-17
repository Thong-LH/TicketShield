@echo off
title TicketShield Microservices Launcher
cd /d "%~dp0"
echo ==========================================================
echo       TICKETSHIELD MICROSERVICES 1-CLICK LAUNCHER
echo ==========================================================
powershell -ExecutionPolicy Bypass -File "%~dp0scripts\start-all.ps1"
pause

