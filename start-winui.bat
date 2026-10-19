@echo off
setlocal
cd /d "%~dp0"

rem Normal startup runs the existing app without PowerShell or source scans.
rem Use start-winui.bat --build after changing application code.
if /I "%~1"=="--build" goto build
if not "%~1"=="" goto helper
if not exist "%~dp0apps\desktop-winui\bin\Debug\net9.0-windows10.0.26100.0\win-x64\CircuitAssistant.exe" goto helper
start "" /D "%~dp0apps\desktop-winui\bin\Debug\net9.0-windows10.0.26100.0\win-x64" "%~dp0apps\desktop-winui\bin\Debug\net9.0-windows10.0.26100.0\win-x64\CircuitAssistant.exe"
exit /b %errorlevel%

:build
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Start-CircuitAssistant.ps1" -ForceBuild
goto result

:helper
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Start-CircuitAssistant.ps1" %*

:result
if errorlevel 1 (
    echo.
    echo CircuitAssistant could not be started.
    pause
    exit /b 1
)

endlocal
