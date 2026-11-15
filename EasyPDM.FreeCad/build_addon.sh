#!/usr/bin/env bash
# Assembles the FreeCAD add-on as FreeCAD's Addon Manager installs it, in FreeCAD's namespaced
# layout: package.xml, LICENSE and README at the top of OUT_DIR, the code in freecad/EasyPDM/
# (FreeCAD imports freecad.EasyPDM.init_gui itself, so nothing touches sys.path). One script for
# both ways the add-on ships -- the pawelcel/EasyPDM-FreeCAD repository and the
# EasyPDM_FreeCad_Addon_vX.zip release asset -- so the two never differ.
#
#   build_addon.sh OUT_DIR [VERSION]     VERSION (e.g. 0.9.0) goes into package.xml with today's date
set -euo pipefail
out="$1"; version="${2:-}"
here="$(cd "$(dirname "$0")" && pwd)"
pkg="$out/freecad/EasyPDM"
mkdir -p "$pkg/templates"
cp "$here/package.xml" "$out/"
cp "$here/../LICENSE" "$out/LICENSE"
cp "$here/ADDON-README.md" "$out/README.md"
printf '# EasyPDM add-on for FreeCAD (see init_gui.py).\n' > "$pkg/__init__.py"
cp "$here"/{init_gui.py,easypdm_addon.py,EasyPDMUpload.FCMacro,EasyPDMDownload.FCMacro} "$pkg/"
cp -r "$here/icons" "$pkg/"
cp "$here"/templates/*.svg "$pkg/templates/"
if [ -n "$version" ]; then
  sed -i -E "s|<version>[^<]*</version>|<version>${version}</version>|; s|<date>[^<]*</date>|<date>$(date -u +%Y-%m-%d)</date>|" "$out/package.xml"
fi
