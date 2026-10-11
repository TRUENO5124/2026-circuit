@echo off
cd /d "%~dp0"

echo ============================================
echo  Circuit Analysis - Start All
echo ============================================
echo.
echo   Starting in new windows:
echo     - API Server  port 3000
echo     - Editor      port 5173
echo.
echo ============================================

:: Launch API in new window
start "Circuit-API" "%~dp0start-api.bat"

:: Wait ~3 seconds for API to warm up
echo   Waiting for API to start - 3s...
ping -n 4 127.0.0.1 >nul

:: Launch Editor in new window
start "Circuit-Editor" "%~dp0start-editor.bat"

echo.
echo   ===========================================
echo   Both services launched in separate windows:
echo.
echo     API Docs : http://localhost:3000/docs
echo     Editor   : http://localhost:5173
echo.
echo   Press Ctrl+C in each window to stop.
echo   Closing this window is safe.
echo   ===========================================

pause
