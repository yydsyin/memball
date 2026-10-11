@echo off
rem Builds MemBall.exe with the C# compiler that ships with .NET Framework 4.x (no SDK needed).
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo csc.exe not found. .NET Framework 4.x is required.
  exit /b 1
)
rem MemBall.ico is the logo; regenerate it with: MemBall.exe --ico MemBall.ico
"%CSC%" /nologo /target:winexe /optimize /win32icon:"%~dp0MemBall.ico" /out:"%~dp0MemBall.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll "%~dp0MemBall.cs"
if errorlevel 1 exit /b 1
echo Built %~dp0MemBall.exe
