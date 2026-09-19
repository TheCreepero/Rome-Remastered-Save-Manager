param(
    [switch]$CreateDesktopShortcut,
    [switch]$SelfContained = $true
)

$ErrorActionPreference = "Stop"
$rootDir = $PSScriptRoot
$projectPath = Join-Path $rootDir "Rome Remastered Save Manager\RRM-SM.UI\RRM-SM.UI.csproj"
$distDir = Join-Path $rootDir "dist"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  Building Standalone Package for Rome Remastered Save Manager  " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

if (Test-Path $distDir) {
    Remove-Item -Recurse -Force $distDir
}

$publishArgs = @(
    "publish",
    "`"$projectPath`"",
    "-c", "Release",
    "-r", "win-x64",
    "-o", "`"$distDir`"",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true"
)

if ($SelfContained) {
    Write-Host "`n--> Packaging Self-Contained standalone executable (no .NET runtime required on target machine)..." -ForegroundColor Yellow
    $publishArgs += "--self-contained", "true"
} else {
    Write-Host "`n--> Packaging Framework-Dependent executable..." -ForegroundColor Yellow
    $publishArgs += "--self-contained", "false"
}

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

$exePath = Join-Path $distDir "RRM-SM.UI.exe"
if (Test-Path $exePath) {
    # Rename to friendly RomeRemasteredSaveManager.exe
    $friendlyExe = Join-Path $distDir "RomeRemasteredSaveManager.exe"
    if (Test-Path $friendlyExe) { Remove-Item -Force $friendlyExe }
    Rename-Item $exePath "RomeRemasteredSaveManager.exe"
    $targetExe = $friendlyExe
} else {
    $targetExe = Join-Path $distDir "RomeRemasteredSaveManager.exe"
}

Write-Host "`n[SUCCESS] Standalone package created!" -ForegroundColor Green
Write-Host "Executable location: $targetExe" -ForegroundColor White

if ($CreateDesktopShortcut) {
    try {
        $wsh = New-Object -ComObject WScript.Shell
        $desktop = [System.Environment]::GetFolderPath('Desktop')
        $shortcutPath = Join-Path $desktop "Rome Remastered Save Manager.lnk"
        $shortcut = $wsh.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = $targetExe
        $shortcut.WorkingDirectory = $distDir
        $shortcut.Description = "Rome Remastered Save Manager & Backup Utility"
        $shortcut.Save()
        Write-Host "[SUCCESS] Desktop shortcut created: $shortcutPath" -ForegroundColor Green
    } catch {
        Write-Warning "Could not create Desktop shortcut: $_"
    }
}

Write-Host "`nYou can copy the 'dist' folder anywhere or run RomeRemasteredSaveManager.exe directly.`n" -ForegroundColor Cyan

