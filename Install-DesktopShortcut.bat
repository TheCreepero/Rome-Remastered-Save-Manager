@echo off
setlocal
cd /d "%~dp0"

echo Building standalone Rome Remastered Save Manager release and creating Desktop Shortcut...
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-Release.ps1" -CreateDesktopShortcut
if errorlevel 1 (
    echo.
    echo [ERROR] Build failed.
    pause
    exit /b 1
)

echo.
echo ========================================================
echo Done! You can now launch the app from your Desktop or
echo run dist\RomeRemasteredSaveManager.exe directly.
echo ========================================================
pause

