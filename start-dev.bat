@echo off
title TicketShield Microservices Launcher
echo ==========================================================
echo       TICKETSHIELD MICROSERVICES 1-CLICK LAUNCHER
echo ==========================================================
powershell -ExecutionPolicy Bypass -File scripts/start-all.ps1
pause
