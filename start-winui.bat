@echo off
cd /d "%~dp0"

echo ========================================================
echo   Circuit Learning Assistant (WinUI 3 Native Desktop)
echo ========================================================
echo.

set "EXE_PATH=%~dp0apps\desktop-winui\bin\Debug\net9.0-windows10.0.26100.0\win-x64\CircuitAssistant.exe"

if exist "%EXE_PATH%" (
    echo Launching native desktop application...
    start "" "%EXE_PATH%"
    echo Application started successfully.
) else (
    echo Building and launching application...
    dotnet run --project "%~dp0apps\desktop-winui\CircuitAssistant.csproj"
)

echo.
