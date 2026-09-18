@echo off
setlocal
cd /d "%~dp0"

:: Check if build exists, if not build it first
set "EXE_PATH=Rome Remastered Save Manager\RRM-SM.UI\bin\Debug\net10.0-windows\RRM-SM.UI.exe"

if not exist "%EXE_PATH%" (
    echo Building Rome Remastered Save Manager UI...
    dotnet build "Rome Remastered Save Manager\RRM-SM.UI\RRM-SM.UI.csproj" -c Debug
    if errorlevel 1 (
        echo.
        echo [ERROR] Build failed. Make sure .NET 10 SDK is installed.
        pause
        exit /b 1
    )
)

:: Launch the UI detached so the terminal window doesn't stay open
start "" "%EXE_PATH%"
exit /b 0

