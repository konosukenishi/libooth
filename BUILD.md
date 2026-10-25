# Release ZIP and VPM distribution

The repository root is the Unity package `dev.konosuke.libooth`. The development
project lives in `Tests~/UnityProject` and references the root via a local package.

## GitHub Actions

`.github/workflows/release.yml` builds on pull requests, manual runs, and `v*` tags.
Only tags publish a GitHub Release. The tag must equal `v` + `package.json.version`.
Prerelease tags create prereleases; OpenVPM currently collects stable releases only.

The native build job runs a matrix on macOS (`aarch64-apple-darwin`) and Windows
(`x86_64-pc-windows-msvc`). A Linux packaging job downloads both native artifacts
and creates one VPM ZIP. Neither dylibs nor DLLs are tracked in Git. Both targets
are built on every workflow run; only downloaded Cargo sources are cached, not
compiled artifacts. Shared importer templates are stored in
`Native~/plugin-metadata/` for both platforms. The workflow uses:

- `dtolnay/rust-toolchain@1.98.0` to install the reviewed Rust toolchain and each platform target.
- Cargo with `--locked --release` and each matrix target to build plugins.
- A staging directory containing `Editor/`, its metadata, `package.json`, and README.
- Static license notices copied from `Native~/licenses/`.
- The standard `zip` command to archive the staging directory, following the
  [VRChat official template](https://github.com/vrchat-community/template-package/blob/main/.github/workflows/release.yml).
- `softprops/action-gh-release@v2.5.3` to create the Release and attach its ZIP.

The release asset is `dev.konosuke.libooth-<version>.zip`, with `package.json` at
its root. Existing asset files are not overwritten. Non-tag builds upload an
Actions artifact for inspection without publishing a Release.

## Local macOS build

Requirements: Apple Silicon Mac, Xcode command-line tools, Rust 1.98.0, CMake,
`jq`, and `zip`. Unity and Unity license credentials are not required for packaging.
Use a clean `Build/package` directory for each staging run.

```sh
rustup toolchain install 1.98.0 --profile minimal --target aarch64-apple-darwin
CARGO_TARGET_DIR=Build/native cargo +1.98.0 build --manifest-path Native~/Cargo.toml --locked --release --target aarch64-apple-darwin
mkdir -p Build/package
cp -R Editor Editor.meta package.json package.json.meta README.md Build/package/
mkdir -p Build/package/Editor/Plugins/macOS
cp Native~/plugin-metadata/Plugins.meta Build/package/Editor/
cp Native~/plugin-metadata/macOS.meta Build/package/Editor/Plugins/
cp Native~/plugin-metadata/macOS/*.meta Build/package/Editor/Plugins/macOS/
cp Build/native/aarch64-apple-darwin/release/libwebview.dylib Build/package/Editor/Plugins/macOS/
cp Build/native/aarch64-apple-darwin/release/libsecrets.dylib Build/package/Editor/Plugins/macOS/
mkdir -p Build/package/Native~
cp -R Native~/licenses Native~/licenses.meta Build/package/Native~/
(cd Build/package && zip -r ../dev.konosuke.libooth-0.1.0.zip .)
unzip -t Build/dev.konosuke.libooth-0.1.0.zip
```

Update the output filename and manifest URL when releasing another version;
the workflow derives both from the manifest automatically.
Copy built dylibs and the matching file/folder `.meta` templates into
`Editor/Plugins/macOS/`, `Editor/Plugins/` and `Editor/` for local Unity development.
These assembled plugin outputs are ignored by Git.
The local commands above produce a macOS-only ZIP. The CI packaging job merges
both platforms before archiving.

## Local Windows build

Use Windows x64 with Rust 1.98.0, Visual Studio 2022 C++ Build Tools (Windows SDK
included), and CMake. In PowerShell:

```powershell
$env:CARGO_TARGET_DIR = "$PWD/Build/native"
$env:CARGO_TARGET_X86_64_PC_WINDOWS_MSVC_RUSTFLAGS = "-C target-feature=+crt-static"
$env:CMAKE_TOOLCHAIN_FILE = "$PWD/Native~/cmake/windows-msvc.cmake" -replace '\\', '/'
cargo +1.98.0 build --manifest-path Native~/Cargo.toml --locked --release --target x86_64-pc-windows-msvc
New-Item -ItemType Directory -Force Editor/Plugins/Windows
Copy-Item Build/native/x86_64-pc-windows-msvc/release/webview.dll Editor/Plugins/Windows/
Copy-Item Build/native/x86_64-pc-windows-msvc/release/secrets.dll Editor/Plugins/Windows/
Copy-Item Native~/plugin-metadata/Windows/*.meta Editor/Plugins/Windows/
Copy-Item Native~/plugin-metadata/Windows.meta Editor/Plugins/
Copy-Item Native~/plugin-metadata/Plugins.meta Editor/
```

SDL and the WebView2 loader are linked statically into the plugin. The Windows
Rust/C runtime is built with static CRT. `Native~/cmake/windows-msvc.cmake`
explicitly selects CMake's static MSVC runtime for SDL to match Rust's
`+crt-static`; otherwise modern CMake defaults SDL to the dynamic CRT and
linking fails with unresolved `__imp_*` symbols. No separate SDL3.dll or WebView2Loader.dll
is packaged. Microsoft Edge WebView2 Evergreen Runtime must be installed on the
user's Windows machine; the full browser runtime is not bundled. WebView2's
working data is stored under `%LOCALAPPDATA%/dev.konosuke.libooth/WebView2`.

The Windows CI job loads both DLLs and checks all managed-entry-point exports
before uploading them. Interactive login and cookie retrieval still require a
Windows Unity Editor check. Unity importer metadata restricts each plugin to its
matching OS/architecture and Editor use.

## VPM listing and dependencies

The `konosukenishi/vpm` repository collects the Release ZIP via OpenVPM and uses
`upmRegistries` with `https://package.openupm.com` to resolve external dependencies.
Keep the source manifest's UPM `dependencies`: OpenVPM converts them recursively
into `vpmDependencies` in its generated packages. It republishes Libooth as well
as the separate dependency ZIPs at `https://vpm.konosuke.dev/`.

Consumers should add `https://vpm.konosuke.dev/index.json` in their VPM client.
The original Release ZIP is an OpenVPM input, not a dependency-bundled installer.

All ten audited OpenUPM dependencies declare MIT. Their exact NuGet artifacts,
checksums, copyright notices and third-party notices are recorded in the VPM
repository's `licenses/UPM/`. Its postprocessing step adds the missing notices to
converted ZIPs and refreshes index checksums. New dependency versions need review.

Native dependency notices are maintained in `Native~/licenses/` and
copied into the ZIP. They include reviewed Rust dependencies, SDL's bundled C
code, Windows-specific dependencies, the WebView2 SDK loader license and the Rust
1.98.0 standard library. No notice collection script or
dependency source archive is used. Review the notices when changing Cargo.lock,
features, SDL or the pinned Rust toolchain. CI checks the reviewed Cargo.lock
fingerprint before building. See `Native~/LICENSE-AUDIT.md` for the macOS scope
and why option-ext's MPL-covered code is excluded in this build.
Third-party notices do not assign a license to Libooth's own code.

## Validation

Check native compilation, archive integrity/root paths, plugin architectures and
license/source inclusion before publishing. In Unity, check compilation, library
loading, login, account migration and importing into a clean project. Packaging
checks do not exercise interactive BOOTH login or the operating system keychain.
The native distribution supports macOS arm64 Editor and Windows x64 Editor.
