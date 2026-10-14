@echo off
rem Produit dist\GreenshotUpdater.exe, seul fichier livré :
rem   lanceur .NET Framework 4.8 qui embarque l'application GreenshotUpdater.App.exe
rem   (exe unique sans runtime embarqué ; le lanceur vérifie le runtime .NET Desktop 10).
setlocal
chcp 65001 >nul
cd /d "%~dp0"
set APP=build\app
set LAUNCHER=build\launcher
if not exist assets\icon.ico (
    dotnet run build_tools\MakeIcon.cs || exit /b 1
)
if exist %APP% rmdir /s /q %APP%
if exist %LAUNCHER% rmdir /s /q %LAUNCHER%
if exist dist rmdir /s /q dist
dotnet publish src\GreenshotUpdater -c Release -o %APP% || exit /b 1
dotnet publish src\GreenshotUpdater.Launcher -c Release -o %LAUNCHER% "-p:AppExe=%CD%\%APP%\GreenshotUpdater.App.exe" || exit /b 1
mkdir dist
copy /y %LAUNCHER%\GreenshotUpdater.exe dist\ >nul || exit /b 1
echo.
echo Exécutable : dist\GreenshotUpdater.exe
