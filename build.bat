@echo off
rem Build ABI-TBS-Blocker.exe with the C# compiler that ships with Windows (.NET Framework 4).
rem Output: dist\ABI-TBS-Blocker.exe
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo csc.exe not found. .NET Framework 4 is required.
  goto :fail
)
if not exist dist mkdir dist
"%CSC%" /nologo /codepage:65001 /optimize+ /platform:anycpu /target:winexe /win32manifest:src\app.manifest /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll /r:System.Core.dll /r:Microsoft.CSharp.dll /out:dist\ABI-TBS-Blocker.exe src\ABITbsBlocker.cs
if errorlevel 1 goto :fail
echo Build OK: dist\ABI-TBS-Blocker.exe
powershell -NoProfile -Command "'SHA256: ' + (Get-FileHash 'dist\ABI-TBS-Blocker.exe' -Algorithm SHA256).Hash"
exit /b 0
:fail
echo Build FAILED.
pause
exit /b 1
