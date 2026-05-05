@echo off
title AI Vision Coach - YOLO Tracking Server
cd /d "%~dp0"

echo ============================================
echo   AI Vision Coach - YOLO Tracking Server
echo   Port: 5003
echo ============================================
echo.

REM Check if Python is available
python --version >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: Python is not installed or not in PATH.
    echo Please install Python 3.8+ from https://python.org
    pause
    exit /b 1
)

echo [1/2] Installing dependencies...
pip install flask opencv-python numpy ultralytics --quiet
if %errorlevel% neq 0 (
    echo WARNING: Some packages may not have installed correctly.
    echo Try manually: pip install flask opencv-python numpy ultralytics
)

echo.
echo [2/2] Starting YOLO server...
echo      Open http://localhost:5003/status to verify.
echo      Press Ctrl+C to stop.
echo.

python FaceID\yolo_tracking_server.py

pause
