@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

echo ============================================
echo  Circuit Analysis - Start API Server
echo ============================================
echo.

:: Check Python
python --version >nul 2>&1
if !errorlevel! neq 0 (
    echo [ERROR] Python not found. Please install Python 3.10+
    pause
    exit /b 1
)
for /f "tokens=2" %%v in ('python --version 2^>^&1') do echo   Python: %%v

:: Check uvicorn
python -c "import uvicorn" >nul 2>&1
if !errorlevel! neq 0 (
    echo.
    echo [1/3] Installing Python dependencies...
    python -m pip install -r "%CD%\services\api\requirements.txt" -q
    if !errorlevel! neq 0 (
        echo [ERROR] pip install failed
        pause
        exit /b 1
    )
    python -m pip install -r "%CD%\services\engine\requirements.txt" -q
) else (
    echo [1/3] Python dependencies OK.
)

:: Create data directories
echo [2/3] Creating data directories...
if not exist "%CD%\services\api\data"          mkdir "%CD%\services\api\data"
if not exist "%CD%\services\api\data\uploads"  mkdir "%CD%\services\api\data\uploads"

:: Start server
echo [3/3] Starting FastAPI server...
echo.
echo   API      : http://localhost:3000
echo   Docs     : http://localhost:3000/docs
echo   Health   : http://localhost:3000/api/health
echo.
echo   Press Ctrl+C to stop
echo ============================================
echo.

python -m uvicorn services.api.main:app --host 0.0.0.0 --port 3000 --reload

pause
