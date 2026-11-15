# EasyPDM for SolidCraft

Work in progress for EasyPDM 0.9.

SolidCraft plugins are WebAssembly components with **no network access** (SolidCraft's plugin
RFC reserves it for signed plugins from a future curated index; EasyPDM asked for a path for
self-hosted servers in tangency-labs/solidcraft#17343). So the integration has two parts:

- **`plugin/`** — the SolidCraft plugin (Rust, `craft:plugin@0.1.0`). Its File-menu entries
  prepare an upload in a folder the user grants at install time (the *outbox*): the design as
  `.sc`, a STEP of it and a `<id>.request.json` with the design properties (part number,
  revision…) and EasyPDM's link to the item, kept under the design metadata key
  `io.github.pawelcel.easypdm`.
- **companion** (to do) — a small EasyPDM program on the same computer that picks requests up,
  talks to the EasyPDM server like the FreeCAD and SolidWorks macros do, and opens downloaded
  designs in SolidCraft through its local control channel.

SolidCraft has no drawing workspace yet, so there is no title block or drawing PDF for it.

## Building the plugin

```bash
plugin/build.sh      # needs Rust (target wasm32-unknown-unknown) and wasm-tools
```

Install `plugin/easypdm.zip` in SolidCraft under Preferences > Plugins and grant it the `file`
command group and the `outbox` folder (read and write: SolidCraft's file commands only see
folders granted both).

## Tested so far

- SolidCraft 0.42.0 installs, inspects and runs the plugin headless; it refuses the `file` group
  there because 0.42 grants plugins only through the Preferences window.
- With the 0.42 nightly (2026-11-15), which grants through `plugin.install`, the upload entry
  writes the `.sc`, the STEP and the request; the `.sc` reopens in 0.42 with its body intact.

`plugin/wit/plugin.wit` is SolidCraft's plugin interface, copied unchanged from
[tangency-labs/solidcraft](https://github.com/tangency-labs/solidcraft)
(`crates/craft-plugin/wit/plugin.wit`, Apache License 2.0).
