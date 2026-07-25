@echo off
title Judas Encoding Manager - Build
echo ========================================
echo   Judas Encoding Manager Build Script
echo ========================================
echo.

echo [1/3] Restoring NuGet packages...
dotnet restore
if errorlevel 1 goto :error

echo [2/3] Building project...
dotnet build -c Release --no-restore
if errorlevel 1 goto :error

echo [3/3] Publishing single-file executable...
dotnet publish -c Release --no-build -o ".\publish"
if errorlevel 1 goto :error

echo.
echo ========================================
echo   Build Complete!
echo ========================================
echo.
echo   Executable location:
echo   %~dp0publish\JudasEncodingManager.exe
echo.
echo   Press any key to open the publish folder...
pause >nul
explorer.exe "%~dp0publish"
goto :eof

:error
echo.
echo ========================================
echo   BUILD FAILED!
echo ========================================
echo.
pause
exit /b 1
