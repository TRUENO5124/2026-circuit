@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

echo ============================================
echo  Circuit Analysis - Start Editor
echo ============================================
echo.

:: Check Node.js
node --version >nul 2>&1
if !errorlevel! neq 0 (
    echo [ERROR] Node.js not found. Please install Node.js 18+
    pause
    exit /b 1
)
for /f "tokens=1 delims=v" %%v in ('node --version 2^>^&1') do echo   Node.js: v%%v

:: Install deps if needed
if not exist "%CD%\node_modules" (
    echo.
    echo [1/2] Installing npm dependencies - first run, may take a while...
    call npm install
    if !errorlevel! neq 0 (
        echo [ERROR] npm install failed
        pause
        exit /b 1
    )
) else (
    echo [1/2] npm dependencies OK.
)

:: Check KaTeX - added by I9
if not exist "%CD%\node_modules\katex" (
    echo        katex missing, re-installing...
    call npm install
)

:: Start Vite
echo [2/2] Starting Vite dev server...
echo.
echo   Editor   : http://localhost:5173
echo   API proxy: /api -^> http://localhost:3000
echo   Make sure start-api.bat is running first
echo.
echo   Press Ctrl+C to stop
echo ============================================
echo.

cd /d "%CD%\packages\editor"
call npx vite --host

pause
