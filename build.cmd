@echo off
rem Kompiluje Jasnosc.exe kompilatorem wbudowanym w Windows (.NET Framework 4.x) - nic nie trzeba instalowac.
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /out:Jasnosc.exe /win32manifest:app.manifest ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Management.dll ^
  src\*.cs
if errorlevel 1 (echo. & echo Kompilacja NIEUDANA & exit /b 1)
echo Zbudowano Jasnosc.exe
