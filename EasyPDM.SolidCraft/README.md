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
  `<id>.result.json` and the processed files move to `outbox/done`. Downloads are saved in
  `Documents/EasyPDM/designs` with the item link written in and opened in SolidCraft through its
  local control channel; login and logout come from the plugin's menu too, approved in the
  browser.

SolidCraft has no drawing workspace yet, so there is no title block or drawing PDF for it.

## Setting it up on a computer

1. `easypdm-companion login --server http://host:port` — allow the sign-in in the browser.
2. `easypdm-companion setup-solidcraft --plugin easypdm.zip` — writes
   `Documents/EasyPDM/easypdm-solidcraft.zip`, the plugin with this computer's outbox path set
   in its upload form (SolidCraft takes only absolute paths inside a granted folder, and a
   plugin cannot learn its grant's path).
3. SolidCraft: Preferences > Plugins > Install plugin…, that file; allow the `file` command
   group and the `outbox` folder, read and write (SolidCraft's file commands see only folders
   granted both).
4. `easypdm-companion autostart`, so `watch` runs whenever you are logged in.
5. To open downloads in SolidCraft: Preferences > Assistant > Let AI agents connect.

The File menu then has **EasyPDM: Upload / Download / Log in / Log out**. The menu entries only
leave requests in the outbox; the companion does the work.

## Building the plugin

```bash
plugin/build.sh      # needs Rust (target wasm32-unknown-unknown) and wasm-tools
```

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
- The whole menu in one headless session against the test server, with the companion in
  `watch`: Log in (approved in the browser), Upload (new item through the ticket), Download
  (picked in the browser, saved with the link; opening checked against a stand-in control
  channel, since no SolidCraft window was running), Log out (token revoked on the server).
- Not yet: the plugin in SolidCraft's own window, installed through Preferences.

`plugin/wit/plugin.wit` is SolidCraft's plugin interface, copied unchanged from
[tangency-labs/solidcraft](https://github.com/tangency-labs/solidcraft)
(`crates/craft-plugin/wit/plugin.wit`, Apache License 2.0).
