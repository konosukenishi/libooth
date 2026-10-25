# Native dependency notices

These reviewed files are copied into the Release ZIP as `Native~/licenses/`.
They cover Cargo.lock for macOS arm64 and Windows x64, SDL 3.4.18 and the Rust 1.98.0 standard
library. Runtime alternatives use MIT where available; dpi uses Apache-2.0 and
bundled HIDAPI uses BSD. Original source-header notices are preserved in NOTICE.md.
RUST-STDLIB.html is the original Rust distribution's standard library notice file,
including its embedded third-party license texts. MIT.txt and Apache-2.0.txt
supply full license texts referenced by upstream licensing explanations.

No dependency executable source is bundled. option-ext's MPL-covered functions
are excluded by the macOS cfg; see ../LICENSE-AUDIT.md. Review this conclusion
when adding another OS. Some conservatively retained notices concern bundled
upstream code that may be unused on macOS; their presence does not select an
alternative license (such as HIDAPI's GPL option).

When changing Cargo.lock, features, SDL or the pinned Rust toolchain, review and
update these files and Cargo.lock.sha256. CI checks the lockfile fingerprint
instead of automatically collecting notices. Keep original copyright notices,
permission text and disclaimers. No Python tooling is needed to build a release.

Windows-specific additions are recorded in WINDOWS-NOTICES.md. WebView2's Rust
bindings use WebView2-Rust-MIT.txt; the static Microsoft SDK loader uses
Microsoft-WebView2-SDK-LICENSE.txt. The loader was verified byte-for-byte against
Microsoft.Web.WebView2 1.0.3800.47. The Evergreen browser runtime itself is not
redistributed in this package.
