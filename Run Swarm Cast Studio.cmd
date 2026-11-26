@echo off
setlocal
set "APP=%~dp0src\SwarmCastStudio\publish-selfcontained\SwarmCastStudio.exe"
if not exist "%APP%" set "APP=%~dp0src\SwarmCastStudio\publish\SwarmCastStudio.exe"
if not exist "%APP%" (
    echo Swarm Cast Studio has not been published yet.
    echo Run: powershell -ExecutionPolicy Bypass -File "%~dp0tools\publish.ps1"
    pause
    exit /b 1
)
start "" "%APP%"
