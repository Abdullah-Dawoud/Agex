# Platform compatibility

Date: 2026-09-24, AGEX 2.0.0 (updated after the v2.0.0 pre-release). Real-device testing: one Windows 10 Pro 22H2 x64 machine. No Mac, Linux desktop or ARM64 device was available. GitHub Actions runners provided build, test-suite and command-line evidence for the other platforms (see [release-2.0.0.md](release-2.0.0.md)).

## Status words

| Status | Meaning |
| --- | --- |
| **TESTED** | Run on real hardware during this pass, with the result checked. |
| **BUILD VERIFIED** | The self-contained package for that platform was compiled and packaged successfully; the program was not run there. |
| **CI TESTED** | Run on a GitHub-hosted runner of that OS and processor, without a desktop session: the command line (`agex version`, `doctor`, `skills catalog`) and, where stated, the test suite and the one-command installer. Not a GUI test. |
| **SUPPORTED BY SOURCE** | Implemented with platform-specific code for that OS, covered by the platform-independent tests, not run there. |
| **BLOCKED** | Cannot work until something outside AGEX is provided (named in the cell). |
| **UNSUPPORTED** | Not available on that platform, and AGEX says so in its interface. |

Windows 11 was not tested separately; AGEX uses no Windows 11-only API.

## Matrix

| Feature | Windows x64 | Windows ARM64 | macOS Intel | macOS Apple Silicon | Linux x64 / ARM64 |
| --- | --- | --- | --- | --- | --- |
| Package builds | TESTED (built and installed) | BUILD VERIFIED on Windows CI | BUILD VERIFIED on macOS CI (ad-hoc signed) | BUILD VERIFIED on macOS CI (ad-hoc signed) | BUILD VERIFIED on Linux CI |
| One-command installer against the published release | TESTED (real machine, scratch folder) and CI TESTED | CI TESTED (`windows-11-arm`) | CI TESTED (`macos-15-intel`) | CI TESTED (`macos-latest`) | CI TESTED (x64 and `ubuntu-24.04-arm`) |
| DMG | — | — | BUILD VERIFIED (not opened) | BUILD VERIFIED (not opened) | — |
| Desktop app starts, all pages | TESTED | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE (X11/Wayland through XWayland) |
| Light / dark / high contrast, text size | TESTED | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE |
| Follows system theme | TESTED | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE (depends on desktop environment) |
| Keyboard navigation, shortcuts, command palette | TESTED (Ctrl) | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE (Cmd) | SUPPORTED BY SOURCE (Cmd) | SUPPORTED BY SOURCE |
| Native menu bar | — (in-window navigation) | — | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE | — |
| Screen reader names | SUPPORTED BY SOURCE (automation names set; not tested with Narrator) | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE (VoiceOver) | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE (AT-SPI through Avalonia.FreeDesktop.AtSpi) |
| `agex` command line (packaged) | TESTED | CI TESTED | CI TESTED | CI TESTED | CI TESTED (x64 and ARM64) |
| Test suite (66 tests) | TESTED and CI TESTED | not run | not run | CI TESTED (`macos-latest`) | CI TESTED (x64 only) |
| Data folders | TESTED (`%LOCALAPPDATA%\AGEX`) | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE (`~/Library/Application Support/AGEX`) | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE (XDG) |
| Secure storage | TESTED (DPAPI, unit test) | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE (Keychain via `security`) | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE (`secret-tool`); without a Secret Service AGEX falls back to a user-only, unencrypted file and says so in Diagnostics |
| Notifications | TESTED (in-app); taskbar flash SUPPORTED BY SOURCE | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE (`osascript`) | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE (`notify-send`) |
| Start with computer | SUPPORTED BY SOURCE (HKCU Run key; not exercised) | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE (LaunchAgent) | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE (XDG autostart) |
| Open folder / file / terminal | SUPPORTED BY SOURCE (not exercised in this pass) | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE (`open`, Terminal) | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE (`xdg-open`, first terminal found) |
| Agent discovery from GUI launch | TESTED | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE (reads login-shell PATH) | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE |
| Process runner (UTF-8, cancel, process tree cleanup) | TESTED (tests + real agents) | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE | CI TESTED (test suite) | CI TESTED on x64 (test suite) |
| Codex adapter | TESTED (real account) | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE |
| Antigravity adapter | TESTED (real account) | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE |
| Claude Code adapter (beta) | Installed (2.0.14) but not signed in: detection and the sign-in-required path TESTED with the real CLI; a successful request not tested | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE |
| Gemini CLI adapter (beta) | Installed (0.9.0) but not signed in: detection and the sign-in-required path TESTED with the real CLI (after a fix); a successful request not tested | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE |
| Ollama adapter (beta) | TESTED (real local model) | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE |
| Git snapshots and undo | TESTED | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE |
| Skill install (instruction skills) | TESTED (all 14 downloaded and hash-checked) | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE |
| MCP skills | Configuration tested; servers need Node.js / uv | same | same | same | same |
| Update check and install | Check TESTED against the published v2.0.0 pre-release ("up to date"); install of a newer release not yet possible | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE | SUPPORTED BY SOURCE |
| Code signing | NOT CONFIGURED (unsigned) | NOT CONFIGURED | Ad-hoc only; Developer ID NOT CONFIGURED; not notarized | Ad-hoc only; Developer ID NOT CONFIGURED; not notarized | Not required |

## Notes

1. **Apple Silicon and unsigned code.** macOS on Apple Silicon refuses to run arm64 code that has no signature at all. Packages cross-built on Windows are therefore unusable on those Macs; the release workflow builds macOS packages on a macOS runner and signs them ad-hoc (or with a Developer ID when configured). Without a Developer ID, users must allow AGEX once in System Settings → Privacy & Security ([INSTALL.md](../docs/INSTALL.md#macos-first-launch-of-an-unsigned-build)).
2. **External agents are separate products.** AGEX's platform list for an agent says whether AGEX can drive it on that OS; whether the vendor ships the agent there is shown by discovery (`Not installed`, `Not available on this system`).
3. **CI.** `.github/workflows/ci.yml` builds and runs the full test suite on `windows-latest`, `macos-latest` (Apple Silicon) and `ubuntu-latest` (x64), and on `main` runs the README one-command install on six runners. `.github/workflows/release.yml` builds every package on its own OS and smoke-tests the packaged `agex` command on a runner of the package's OS and processor. All passed for v2.0.0. None of this starts the desktop window.

## What must happen before claiming macOS or Linux support

1. ~~Push and let CI pass on all three OSes.~~ Done.
2. ~~Run the release workflow on a tag.~~ Done (v2.0.0 pre-release).
3. On a real Mac (Intel and Apple Silicon) and a Linux desktop: install, onboarding, one Codex request, one request with a file change and undo, skill install, theme switching, VoiceOver spot check. Checklists: [docs/MAC_TEST.md](../docs/MAC_TEST.md), [docs/LINUX_TEST.md](../docs/LINUX_TEST.md).
4. Update this table to TESTED only for what was run.
