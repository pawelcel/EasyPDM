# EasyPDM for SolidCraft

Work in progress for EasyPDM 0.9.

SolidCraft plugins are WebAssembly components with **no network access** (SolidCraft's plugin
RFC reserves it for signed plugins from a future curated index; EasyPDM asked for a path for
self-hosted servers in tangency-labs/solidcraft#17343). So the integration has two parts:

- **`plugin/`** — the SolidCraft plugin (Rust, `craft:plugin@0.1.0`). Its File-menu entries
  prepare an upload in a folder the user grants at install time (the *outbox*): the design as
  `.sc`, a STEP of it and a `<id>.request.json` with the design properties (part number,
  revision…) and EasyPDM's link to the item, kept under the design metadata key
  `io.github.pawelcel.easypdm`. The first upload gives the design a `designId` and writes
  `<designId>.link.json` into the outbox; the companion fills the item in once the upload has
  gone through, and the next upload reads it into the design, with the record name and
  revision as SolidCraft's own part number and revision. The plugin reads only that file, and
  only until the design knows its item: SolidCraft voids a whole plugin call when one of its
  reads fails, even one the plugin handles, so it never asks for a file that may not exist.
- **[`EasyPDM.Companion`](../EasyPDM.Companion/)** — a small EasyPDM program on the same
  computer (`easypdm-companion login | logout | status | process | watch`). It picks requests
  up and uploads them the way the FreeCAD and SolidWorks macros do: a design already linked to
  an item goes to it (a released item gets a new revision only with a comment), otherwise the
  item is created or picked in the browser through the macros' ticket flow; the design goes up
  as the `cad` attachment `<record name>.<revision>.sc`, with the STEP, a preview from
  `solidcraft-cli snapshot` and the mass from `solidcraft-cli eval`. The outcome is written as
  `<id>.result.json` and the processed files move to `outbox/done`. Downloads (opening a design
  in SolidCraft through its local control channel) are still to do.

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
- The companion, against a test server, with a script standing in for the browser: a new item
  created through the ticket gets the `.sc`, STEP, preview and mass; a linked item in progress
  takes the same revision again; a linked released item is refused without a comment and gets
  revision B with one.
- The round trip in one headless session (0.42 nightly) against the test server: the first
  upload creates the item through the browser ticket, the second writes the link and part
  number into the design and goes to the same item without the browser; two uploads in the
  same second before the companion runs get distinct requests, and both land in one item.

`plugin/wit/plugin.wit` is SolidCraft's plugin interface, copied unchanged from
[tangency-labs/solidcraft](https://github.com/tangency-labs/solidcraft)
(`crates/craft-plugin/wit/plugin.wit`, Apache License 2.0).
