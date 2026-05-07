@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "PAUSE_ON_EXIT=1"
if /I "%~1"=="--no-pause" (
    set "PAUSE_ON_EXIT=0"
    shift
)

call :main %*
set "EXIT_CODE=%ERRORLEVEL%"

if "%PAUSE_ON_EXIT%"=="1" (
    echo.
    if "%EXIT_CODE%"=="0" (
        echo [INFO] Done. Press any key to close this window.
    ) else (
        echo [ERROR] Script failed with exit code %EXIT_CODE%.
        echo [ERROR] Press any key to close this window.
    )
    pause >nul
)

exit /b %EXIT_CODE%

:main
set "IS_ELEVATED_RUN=0"
if /I "%~1"=="--elevated" (
    set "IS_ELEVATED_RUN=1"
    shift
)

set "DOTNET_RUNTIME_NAME=Microsoft.WindowsDesktop.App"
set "DOTNET_CHANNEL=8.0"
set "DOTNET_MAJOR_PREFIX=8."
set "RUNTIME_URL="
set "INSTALLER_PATH=%TEMP%\windowsdesktop-runtime-8-win-x64.exe"
set "SCRIPT_DIR=%~dp0"
set "OFFLINE_INSTALLER_PATH=%SCRIPT_DIR%windowsdesktop-runtime-win-x64.exe"
set "POWERSHELL_EXE="
set "INSTALL_LOG=%TEMP%\fluxram-dotnet-runtime-install.log"

call :resolve_powershell
if not %ERRORLEVEL% EQU 0 (
    echo [ERROR] PowerShell is required but could not be found.
    exit /b 1
)

call :has_runtime
if %ERRORLEVEL% EQU 0 (
    echo [INFO] .NET 8 Desktop Runtime is already installed.
    exit /b 0
)

call :is_admin
if not %ERRORLEVEL% EQU 0 (
    if "%IS_ELEVATED_RUN%"=="1" (
        echo [ERROR] Administrator privileges are required to install .NET runtime.
        exit /b 1
    )

    echo [INFO] Requesting administrator privileges...
    "%POWERSHELL_EXE%" -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -Verb RunAs -FilePath '%ComSpec%' -ArgumentList '/c """"%~f0"""" --elevated --no-pause'" >nul 2>&1
    if not %ERRORLEVEL% EQU 0 (
        echo [ERROR] UAC elevation was cancelled or failed.
        exit /b 1
    )

    echo [INFO] Elevated installer started in a new window.
    set "PAUSE_ON_EXIT=0"
    exit /b 0
)

if exist "%OFFLINE_INSTALLER_PATH%" (
    echo [INFO] Using offline installer: %OFFLINE_INSTALLER_PATH%
    set "INSTALLER_PATH=%OFFLINE_INSTALLER_PATH%"
) else (
    call :resolve_runtime_url
    if not %ERRORLEVEL% EQU 0 (
        exit /b 1
    )

    echo [INFO] Downloading installer...
    echo [INFO] %RUNTIME_URL%
    "%POWERSHELL_EXE%" -NoProfile -ExecutionPolicy Bypass -Command "$ProgressPreference='SilentlyContinue'; Invoke-WebRequest -Uri '%RUNTIME_URL%' -OutFile '%INSTALLER_PATH%'"
    if not %ERRORLEVEL% EQU 0 (
        echo [ERROR] Download failed.
        exit /b 1
    )
)

echo [INFO] Running silent install...
"%INSTALLER_PATH%" /install /quiet /norestart >"%INSTALL_LOG%" 2>&1
set "INSTALL_EXIT=%ERRORLEVEL%"

if "%INSTALL_EXIT%"=="0" (
    echo [INFO] Install completed successfully.
) else if "%INSTALL_EXIT%"=="3010" (
    echo [INFO] Install completed. A reboot is recommended.
) else (
    echo [ERROR] Install failed with exit code %INSTALL_EXIT%.
    echo [ERROR] See log: %INSTALL_LOG%
    if /I "%INSTALLER_PATH%"=="%OFFLINE_INSTALLER_PATH%" (
        rem Keep offline installer
    ) else (
        del /q "%INSTALLER_PATH%" >nul 2>&1
    )
    exit /b %INSTALL_EXIT%
)

if /I "%INSTALLER_PATH%"=="%OFFLINE_INSTALLER_PATH%" (
    rem Keep offline installer
) else (
    del /q "%INSTALLER_PATH%" >nul 2>&1
)

call :has_runtime
if not %ERRORLEVEL% EQU 0 (
    echo [ERROR] Runtime installation verification failed.
    echo [ERROR] See log: %INSTALL_LOG%
    exit /b 1
)

echo [INFO] .NET 8 Desktop Runtime is ready.
exit /b 0

:is_admin
net session >nul 2>&1
exit /b %ERRORLEVEL%

:resolve_powershell
set "POWERSHELL_EXE="
for /f "usebackq delims=" %%P in (`where powershell 2^>nul`) do (
    set "POWERSHELL_EXE=%%P"
    goto :resolve_powershell_done
)

if exist "%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" (
    set "POWERSHELL_EXE=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
)

:resolve_powershell_done
if defined POWERSHELL_EXE (
    exit /b 0
)
exit /b 1

:resolve_runtime_url
set "RUNTIME_URL="
echo [INFO] Resolving latest .NET %DOTNET_CHANNEL% Desktop Runtime (win-x64) URL...
for /f "usebackq delims=" %%U in (`"%POWERSHELL_EXE%" -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference = 'Stop'; $index = Invoke-RestMethod 'https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json'; $channel = $index.'releases-index' | Where-Object { $_.'channel-version' -eq '%DOTNET_CHANNEL%' } | Select-Object -First 1; if (-not $channel) { throw 'Unable to find .NET channel metadata.' }; $releases = Invoke-RestMethod $channel.'releases.json'; $release = $releases.releases | Where-Object { $_.'release-version' -eq $channel.'latest-release' } | Select-Object -First 1; if (-not $release) { throw 'Unable to find latest release metadata.' }; $file = $release.windowsdesktop.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.exe' } | Select-Object -First 1; if (-not $file) { throw 'Unable to resolve windowsdesktop runtime installer URL.' }; $file.url"`) do (
    set "RUNTIME_URL=%%U"
)

if defined RUNTIME_URL (
    exit /b 0
)

set "RUNTIME_URL=https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe"
echo [WARN] Metadata lookup failed. Falling back to alias URL.
exit /b 0

:has_runtime
where dotnet >nul 2>&1
if not %ERRORLEVEL% EQU 0 (
    exit /b 1
)

set "RUNTIME_FOUND="
for /f "usebackq delims=" %%L in (`dotnet --list-runtimes ^| findstr /R /C:"^%DOTNET_RUNTIME_NAME% %DOTNET_MAJOR_PREFIX%"`) do (
    set "RUNTIME_FOUND=%%L"
)

if defined RUNTIME_FOUND (
    set "RUNTIME_FOUND="
    exit /b 0
)

exit /b 1
