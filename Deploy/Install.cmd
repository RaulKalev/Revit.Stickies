@echo off
rem Installs Stickies for the current user (Revit 2024 and/or 2026). Close Revit first.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Stickies.ps1" %*
pause
