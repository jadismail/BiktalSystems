@echo off
echo BiktalSystems IIS setup (HTTPS port 8088) requires Administrator privileges.
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process powershell -Verb RunAs -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File ""%~dp0Setup-IIS.ps1"" -Port 8088 -HostName BiktalSystems'"
pause
