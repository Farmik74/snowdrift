@echo off
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
set RES=/resource:assets\snowdrift.ico,snowdrift.ico
if exist assets\donate-qr.png set RES=%RES% /resource:assets\donate-qr.png,donate-qr.png
if exist assets\donate-url.txt set RES=%RES% /resource:assets\donate-url.txt,donate-url.txt
if not exist dist mkdir dist
"%CSC%" /nologo /codepage:65001 /target:winexe /unsafe /optimize+ /out:dist\Snowdrift.exe /win32icon:assets\snowdrift.ico /win32manifest:app.manifest /r:System.Windows.Forms.dll /r:System.Drawing.dll %RES% src\*.cs
if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)
echo OK: dist\Snowdrift.exe
