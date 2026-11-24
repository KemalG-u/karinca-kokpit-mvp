@echo off
rem Builds KarincaCark.exe and the LaunchProbe test helper with the C# compiler that ships with Windows
rem (.NET Framework 4, C# 5). No package or runtime download.
rem Output: build\KarincaCark.exe and build\checks-bin\LaunchProbe.exe (the built-in checks look for the latter).
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo csc.exe not found: %CSC%
  exit /b 1
)
if not exist build\checks-bin mkdir build\checks-bin
"%CSC%" /nologo /target:winexe /out:build\checks-bin\LaunchProbe.exe src\LaunchProbe.cs
if errorlevel 1 exit /b 1
"%CSC%" /nologo /target:winexe /out:build\KarincaCark.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll src\App.cs src\Core.cs src\CoreChecks.cs src\UiChecks.cs src\Radial.cs src\RadialChecks.cs
exit /b %errorlevel%
