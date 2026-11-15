# EasyPDM companion

A small program on a CAD workstation that does the network part for CAD integrations that
cannot reach the EasyPDM server themselves — today [SolidCraft](../EasyPDM.SolidCraft/),
whose plugins have no network access.

```
easypdm-companion login --server http://host:port   approve the sign-in in the browser
easypdm-companion setup-solidcraft --plugin easypdm-solidcraft-plugin.zip
easypdm-companion autostart                          start `watch --background` when you log in
easypdm-companion watch                              process what the SolidCraft plugin sends
easypdm-companion status | logout | process | download
```

- **Login** goes through the browser (device login, `DeviceLoginEndpoints.cs` on the server):
  the companion shows a code, EasyPDM opens in the browser and you allow it there. It then holds
  its own macro token (client "EasyPDM companion"), listed and revocable in Settings → Users.
  `--password` signs in with the password instead. The token is kept in
  `~/.config/EasyPDM/companion.json` (`%APPDATA%\EasyPDM` on Windows), readable only by you.
- **In the background** (`watch --background`, what `autostart` sets up — the per-user Run key on
  Windows, `~/.config/autostart` on Linux): no console window; the output goes to
  `%LOCALAPPDATA%\EasyPDM\companion.log` (`~/.local/state/EasyPDM/companion.log`), started afresh
  each time, and every outcome — uploaded, or why not — appears as a desktop notification.
- **Outbox**: `Documents/EasyPDM/outbox` (the language-specific Documents folder; the SolidCraft
  Flatpak sees only that folder). Each request is processed once; its files then move to
  `outbox/done` and its outcome is written as `<id>.result.json`.
- **Uploads** follow the FreeCAD and SolidWorks macros: linked designs go to their item, new
  ones are created or picked in the browser through the macros' ticket flow, a released item
  gets a new revision only with a comment. The preview and the mass come from `solidcraft-cli`
  (the Flatpak on Linux, `Program Files\SolidCraft` on Windows; `--solidcraft-cli PATH` to
  override).
- **Downloads** go to `Documents/EasyPDM/designs`, with the item link written into the design,
  and open in the running SolidCraft through its local control channel when that is on.

Releases carry `EasyPDM_Companion_win-x64_vX.zip` and `EasyPDM_Companion_linux-x64_vX.zip`: one
self-contained program (no .NET to install) with the SolidCraft plugin package beside it, which
`setup-solidcraft` picks up by itself.

Build: `dotnet build EasyPDM.Companion` (.NET 10, no other dependencies).
