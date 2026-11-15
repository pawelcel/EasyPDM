@echo off
rem Removes the EasyPDM add-in from SOLIDWORKS. Run as administrator.
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe" /unregister "%~dp0EasyPDM.SolidWorks.Addin.dll"
pause
