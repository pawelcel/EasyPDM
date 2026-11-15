# EasyPDM add-in for SOLIDWORKS

An **EasyPDM** menu and toolbar in SOLIDWORKS with **Upload**, **Download**, **Log in** and
**Log out**. The commands run the EasyPDM macros themselves (`EasyPDMUpload.swp`,
`EasyPDMDownload.swp`) through SOLIDWORKS' `RunMacro2`, so they behave exactly like the
macros; **Log in** is the Upload macro's `LoginInBrowser` (approve the sign-in in the browser),
**Log out** its `Logout`.

## Installing

1. Unpack `EasyPDM_SolidWorks_Addin_vX.zip` into a folder that stays (for example
   `C:\Program Files\EasyPDM\SolidWorks`).
2. Put `EasyPDMUpload.swp` and `EasyPDMDownload.swp` from the same release into that folder
   (the `.swp` files are compiled from the `.bas` macros on a live SOLIDWORKS and added to the
   release by hand).
3. Right-click `install.cmd` → **Run as administrator**. It clears the "downloaded from the
   internet" mark and registers the add-in (`RegAsm /codebase`).
4. Start SOLIDWORKS. The add-in loads at start-up for the user who installed it; others tick
   **EasyPDM** under **Tools → Add-Ins**.

`uninstall.cmd` (as administrator) removes it.

## How it is built

A .NET Framework 4.8 class library (what SOLIDWORKS loads), COM-visible, implementing
`ISwAddin`. It compiles against the SOLIDWORKS 2024 SP1 interop assemblies from NuGet
(`SolidWorks.Interop.*` 32.1.0, signed by Dassault Systèmes SolidWorks Corp.) with their types
embedded, so the add-in ships none of them and binds to the installed SOLIDWORKS. It builds on
Linux too (`dotnet build -c Release`), which is how the release workflow builds it.

Icons are drawn at start-up into `%LOCALAPPDATA%\EasyPDM\SolidWorks`.

## Status

Built, not yet run on SOLIDWORKS.
