@echo off
setlocal
cd /d "%~dp0"

set "CLI_EXE=Rome Remastered Save Manager\RRM-SM\bin\Debug\net10.0\RRM-SM.exe"

if not exist "%CLI_EXE%" (
    echo Building Rome Remastered Save Manager CLI...
    dotnet build "Rome Remastered Save Manager\RRM-SM\RRM-SM.csproj" -c Debug
    if errorlevel 1 (
        echo.
        echo [ERROR] Build failed. Make sure .NET 10 SDK is installed.
        pause
        exit /b 1
    )
)

"%CLI_EXE%" %*

