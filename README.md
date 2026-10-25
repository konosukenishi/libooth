# Libooth

Access and import your BOOTH library directly from the Unity Editor.

Requires Unity 2022.3 or later. Native release packages include macOS Apple Silicon and Windows x64 plugins.
Windows requires the [Microsoft Edge WebView2 Evergreen Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/).

## Install with VPM

Add `https://vpm.konosuke.dev/index.json` to your VPM client, then install
`dev.konosuke.libooth`. Open **Tools > Libooth** in Unity.
The listing becomes available after the first stable GitHub Release and repository rebuild.

OpenVPM republishes the external UPM dependencies as separate VPM packages.
Users of the VPM listing do not need to configure OpenUPM themselves.
GitHub Release ZIPs are inputs to OpenVPM: their original UPM dependencies are
converted in the repository's generated ZIPs and index.

Remove an existing `me.konosuke.libooth` installation before installing from source.
The VPM manifest also declares the old package as a legacy package.
Saved accounts are copied to the new ID only after successful authentication and
decryption, with the old data retained. Download caches start empty under the new ID.

## Develop

Open `Tests~/UnityProject` in Unity 2022.3.22f1. Its local package dependency points
to the repository root. OpenUPM remains configured in this development project.
The `~` directories are excluded from Unity package asset import.

Native source is in `Native~/`; managed code and UI assets are in `Editor/`.
Native binaries are not tracked in Git. The workflow builds both platforms on
every run and combines the results with the `.meta` templates in
`Native~/plugin-metadata/`. For local development, build and copy macOS dylibs to
`Editor/Plugins/macOS/`; for Windows, copy `webview.dll` and `secrets.dll` to
`Editor/Plugins/Windows/`, plus the matching `.meta` templates from
`Native~/plugin-metadata/` (including the folder metadata) before running Unity.
`Editor/Plugins/` and `Editor/Plugins.meta` are ignored build outputs.
Do not run old and new package versions
concurrently while migrating saved accounts.

See [BUILD.md](BUILD.md) for local ZIP generation and release automation.

Native plugin third-party license notices are included at `Native~/licenses/`.
