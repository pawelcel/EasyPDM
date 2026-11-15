@echo off
rem EasyPDM add-in for SOLIDWORKS: registers it. Run as administrator (right-click > Run as administrator).
rem Keep this folder where it is: SOLIDWORKS loads the add-in from here. Put EasyPDMUpload.swp and
rem EasyPDMDownload.swp (compiled from the .bas macros, attached to the release) in the same folder.
setlocal
set "HERE=%~dp0"
rem Files unpacked from a downloaded zip carry the "from the internet" mark, which stops .NET from
rem loading the DLL inside SOLIDWORKS.
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -LiteralPath $env:HERE | Unblock-File"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe" /codebase "%HERE%EasyPDM.SolidWorks.Addin.dll"
if errorlevel 1 (
  echo Registration failed. Run this file as administrator.
  pause
  exit /b 1
)
echo EasyPDM add-in registered. Start SOLIDWORKS: the EasyPDM menu and toolbar appear,
echo or tick EasyPDM under Tools ^> Add-Ins.
pause
