@echo off
setlocal
cd /d "%~dp0"

:: Check if running alongside standalone executable
if exist "RomeRemasteredSaveManager.exe" (
    start "" /d "%~dp0" "RomeRemasteredSaveManager.exe"
    exit /b 0
)

:: If in development repository, build incremental changes
set "CSPROJ=%~dp0Rome Remastered Save Manager\RRM-SM.UI\RRM-SM.UI.csproj"
set "EXE_PATH=%~dp0Rome Remastered Save Manager\RRM-SM.UI\bin\Debug\net10.0-windows\RRM-SM.UI.exe"

if exist "%CSPROJ%" (
    echo Building Rome Remastered Save Manager UI...
    dotnet build "%CSPROJ%" -c Debug
    if errorlevel 1 (
        echo.
        echo [ERROR] Build failed. Make sure .NET 10 SDK is installed.
        pause
        exit /b 1
    )
)

:: Launch the UI detached so the terminal window doesn't stay open
if exist "%EXE_PATH%" (
    start "" "%EXE_PATH%"
) else (
    echo [ERROR] Executable not found at %EXE_PATH%
    pause
    exit /b 1
)
exit /b 0

