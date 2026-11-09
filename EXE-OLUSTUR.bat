@echo off
setlocal
cd /d "%~dp0"
title BAYAT SMART NETWORK OPTIMIZER - EXE BUILDER

echo.
echo  ================================================
echo   BAYAT SMART NETWORK OPTIMIZER - EXE BUILDER
echo  ================================================
echo.

if not exist "%~dp0build-exe.ps1" (
  echo HATA: build-exe.ps1 bulunamadi.
  echo EXE-OLUSTUR.bat ile build-exe.ps1 ayni klasorde olmali.
  echo Su anki klasor: %~dp0
  pause
  exit /b 1
)

if not exist "%~dp0src\SmartNetworkOptimizer\SmartNetworkOptimizer.csproj" (
  echo HATA: Proje dosyasi bulunamadi.
  echo Beklenen yol: src\SmartNetworkOptimizer\SmartNetworkOptimizer.csproj
  echo ZIP dosyasini tum icerigiyle ayiklayin ve BAT dosyasini
  echo SmartNetworkOptimizer ana klasorunun icinden calistirin.
  pause
  exit /b 1
)

where powershell.exe >nul 2>&1
if errorlevel 1 (
  echo HATA: Windows PowerShell bulunamadi.
  pause
  exit /b 1
)

echo .NET 10 SDK ve NuGet paketleri kontrol edilecek.
echo Internet baglantisi gerekir. Yonetici izni istenmez.
echo.
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-exe.ps1" -RuntimeIdentifier win-x64
set "BUILD_EXIT=%ERRORLEVEL%"

if not "%BUILD_EXIT%"=="0" (
  echo.
  echo EXE OLUSTURULAMADI. Hata kodu: %BUILD_EXIT%
  pause
  exit /b %BUILD_EXIT%
)

if not exist "%~dp0artifacts\win-x64\SmartNetworkOptimizer.exe" (
  echo.
  echo HATA: Derleme komutu tamamlandi ancak EXE dosyasi bulunamadi.
  pause
  exit /b 1
)

echo.
echo  ================================================
echo   BAYAT DERLEME BASARILI
echo ================================================
echo.
echo EXE: artifacts\win-x64\SmartNetworkOptimizer.exe
echo ZIP: artifacts klasoru
pause
exit /b 0
