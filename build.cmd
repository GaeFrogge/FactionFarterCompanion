@echo off
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo The Windows .NET Framework compiler could not be found.
    exit /b 1
)
if not exist build mkdir build
"%CSC%" /nologo /target:winexe /out:build\FactionFarterCompanion.exe /win32icon:assets\Skull.ico /win32manifest:App.manifest /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll Companion.cs
if errorlevel 1 exit /b 1
"%CSC%" /nologo /target:winexe /out:build\FactionFarterCompanionSetup.exe /win32icon:assets\Skull.ico /win32manifest:App.manifest /r:System.Windows.Forms.dll /r:System.Drawing.dll /resource:build\FactionFarterCompanion.exe,Companion Setup.cs
if errorlevel 1 exit /b 1
echo Built build\FactionFarterCompanionSetup.exe
