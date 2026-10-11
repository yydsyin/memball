@echo off
rem Builds MemBall.exe (portable) and MemBall-Setup.exe (installer) with the C# compiler
rem that ships with .NET Framework 4.x (no SDK needed).
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo csc.exe not found. .NET Framework 4.x is required.
  exit /b 1
)
set REFS=/r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll

rem MemBall.ico is the logo; regenerate it with: MemBall.exe --ico MemBall.ico
"%CSC%" /nologo /target:winexe /optimize /win32icon:"%~dp0MemBall.ico" /out:"%~dp0MemBall.exe" %REFS% "%~dp0*.cs"
if errorlevel 1 exit /b 1
echo Built %~dp0MemBall.exe

rem The installer carries MemBall.exe and LICENSE inside it.
"%CSC%" /nologo /target:winexe /optimize /win32icon:"%~dp0MemBall.ico" /win32manifest:"%~dp0setup\setup.manifest" /out:"%~dp0MemBall-Setup.exe" /resource:"%~dp0MemBall.exe",MemBall.exe /resource:"%~dp0LICENSE",LICENSE %REFS% "%~dp0setup\Setup.cs" "%~dp0Lang.cs"
if errorlevel 1 exit /b 1
echo Built %~dp0MemBall-Setup.exe
