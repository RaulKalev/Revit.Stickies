@echo off
rem Removes Stickies for the current user.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Stickies.ps1" -Uninstall %*
pause
