@echo off
setlocal EnableExtensions

set "APP_EXE=%~dp0FluxRAM.exe"
if not exist "%APP_EXE%" set "APP_EXE=%~dp0..\dist\fluxram-win-x64\FluxRAM.exe"

set "INSTALL_SCRIPT=%~dp0install-dotnet-desktop-runtime-8.bat"

if not exist "%APP_EXE%" (
    echo [ERROR] FluxRAM executable not found: %APP_EXE%
    echo [ERROR] Press any key to close this window.
    pause >nul
    exit /b 1
)

if exist "%INSTALL_SCRIPT%" (
    call "%INSTALL_SCRIPT%" --no-pause
    if not %ERRORLEVEL% EQU 0 (
        echo [ERROR] Runtime setup failed. FluxRAM was not started.
        echo [ERROR] Press any key to close this window.
        pause >nul
        exit /b 1
    )
) else (
    echo [WARN] Runtime install script not found. Starting app directly.
)

echo [INFO] Starting FluxRAM...
start "" "%APP_EXE%"
exit /b 0
