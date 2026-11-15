#!/usr/bin/env bash
# Assembles the FreeCAD add-on as FreeCAD's Addon Manager installs it: everything at the top of
# OUT_DIR, with the manifest, LICENSE and README. One script for both ways the add-on ships --
# the pawelcel/EasyPDM-FreeCAD repository and the EasyPDM_FreeCad_Addon_vX.zip release asset --
# so the two never differ.
#
#   build_addon.sh OUT_DIR [VERSION]     VERSION (e.g. 0.9.0) goes into package.xml with today's date
set -euo pipefail
out="$1"; version="${2:-}"
here="$(cd "$(dirname "$0")" && pwd)"
mkdir -p "$out" "$out/templates"
cp "$here"/{InitGui.py,Init.py,easypdm_addon.py,package.xml,EasyPDMUpload.FCMacro,EasyPDMDownload.FCMacro} "$out"/
cp -r "$here/icons" "$out"/
cp "$here"/templates/*.svg "$out/templates/"
cp "$here/../LICENSE" "$out/LICENSE"
cp "$here/ADDON-README.md" "$out/README.md"
if [ -n "$version" ]; then
  sed -i -E "s|<version>[^<]*</version>|<version>${version}</version>|; s|<date>[^<]*</date>|<date>$(date -u +%Y-%m-%d)</date>|" "$out/package.xml"
fi
