#!/usr/bin/env bash
# Builds the EasyPDM SolidCraft plugin package: easypdm.zip with plugin.json and the
# WebAssembly component. Needs Rust with the wasm32-unknown-unknown target and wasm-tools.
set -euo pipefail
cd "$(dirname "$0")"
cargo build --release --target wasm32-unknown-unknown
wasm-tools component new target/wasm32-unknown-unknown/release/easypdm_solidcraft.wasm -o easypdm.wasm
rm -rf target/pkg && mkdir -p target/pkg/easypdm
cp plugin.json easypdm.wasm target/pkg/easypdm/
(cd target/pkg && rm -f ../../easypdm.zip && zip -q -r ../../easypdm.zip easypdm)
echo "built $(pwd)/easypdm.zip"
