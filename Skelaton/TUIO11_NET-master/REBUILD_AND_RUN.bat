@echo off
echo ========================================
echo   PADEL COACH - REBUILD AND RUN
echo ========================================
echo.

echo Step 1: Closing old TuioDemo.exe...
taskkill /F /IM TuioDemo.exe 2>nul
if %errorlevel% equ 0 (
    echo [OK] Old application closed
) else (
    echo [INFO] No running application found
)
echo.

echo Step 2: Rebuilding project...
"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" TUIO_DEMO.csproj /t:Rebuild /p:Configuration=Debug /v:minimal
if %errorlevel% neq 0 (
    echo [ERROR] Build failed!
    pause
    exit /b 1
)
echo [OK] Build successful
echo.

echo Step 3: Starting new version...
echo.
echo ========================================
echo   LAUNCHING PADEL COACH
echo ========================================
echo.
start bin\Debug\TuioDemo.exe
echo.
echo [OK] Application started!
echo.
echo You should now see:
echo   - "Advanced Padel Rules" (not "HighSchool Grammar")
echo   - "GOLDEN POINT" (not "PASSIVE VOICE")
echo   - "read the padel term" (not "read the word")
echo.
echo All English content should be replaced with Padel content.
echo.
pause
