# AGEX 2.0.0 release report

Date: 2026-09-24. Repository: https://github.com/Abdullah-Dawoud/Agex (the old name `Abdullah-Dawoud/Ai-COGY` redirects here; the local `origin` still uses the old URL).

## Source

| Item | Value |
| --- | --- |
| SOURCE COMMIT (tagged) | `f24a7f2ad9b65085bcefdb5c1598b7e695f97f1e` |
| REMOTE MAIN at tagging | `f24a7f2ad9b65085bcefdb5c1598b7e695f97f1e` (matched local) |
| PUSH | PASS. Fast-forward from `dfa9ae6`. The two AGEX commits were then reworded to remove an AI co-author line and pushed with `--force-with-lease` (trees unchanged, done at the owner's request), after which every push was a fast-forward. |
| TAG | `v2.0.0` (annotated) on `f24a7f2` |
| Later commits on main | CI-only and report commits after the tag (installer test job, this report); the release packages come from the tag. |

Release-engineering fixes made in this pass (no product features):

1. Installers and in-app updater found only full releases (`releases/latest`), so a pre-release would have been invisible; they now take the newest non-draft release, pre-releases included. The README one-liners now fetch the installer from `main` (`raw.githubusercontent.com`), because `releases/latest/download/…` does not resolve for a pre-release.
2. macOS signing failed on the runner (`code object is not signed at all … agex.runtimeconfig.json`): .NET data files in `Contents/MacOS` are now signed before the main executable.
3. Release workflow: packages are built and smoke-tested on every push to `main`; publishing needs the smoke tests; releases are published as pre-releases unless the repository variable `AGEX_STABLE_RELEASE` is `true`; dedicated release notes (`docs/release-notes/v2.0.0.md`); failures are repeated as annotations (job logs need sign-in to read).
4. CI job that runs the README one-command install against the published release on six runners.
5. Docs corrected: on Linux without a Secret Service, secrets fall back to an unencrypted user-only file (seen on CI), not a refusal.

## CI results

Build = package built by the release workflow on a runner of that OS. Tests = the 66-test suite. Runtime = what actually ran.

| Target | Build | Tests | Runtime |
| --- | --- | --- | --- |
| Windows x64 | PASS (`windows-latest`) | PASS (CI and release build) | **TESTED on a real machine** (desktop app, real agents, earlier pass); released package's `agex` run locally; one-command install run locally into a scratch folder; CLI smoke and install CI PASS |
| Windows ARM64 | PASS (cross-compiled on `windows-latest`) | NOT RUN | CLI smoke PASS and one-command install PASS on `windows-11-arm` runner; **desktop GUI NOT TESTED** |
| macOS x64 (Intel) | PASS (`macos-latest`, ad-hoc signed) | NOT RUN | CLI smoke PASS (incl. `codesign --verify`) and one-command install PASS on `macos-15-intel` runner; **desktop GUI NOT TESTED** |
| macOS arm64 | PASS (`macos-latest`, ad-hoc signed) | PASS (`macos-latest` CI and release build) | CLI smoke PASS (incl. `codesign --verify`) and one-command install PASS on `macos-latest`; **desktop GUI NOT TESTED, no real Mac** |
| Linux x64 | PASS (`ubuntu-latest`) | PASS (CI and release build) | CLI smoke PASS and one-command install PASS on `ubuntu-latest`; **desktop GUI NOT TESTED** |
| Linux arm64 | PASS (cross-compiled on `ubuntu-latest`) | NOT RUN | CLI smoke PASS and one-command install PASS on `ubuntu-24.04-arm`; **desktop GUI NOT TESTED** |

CLI smoke = extract the released package, check the desktop executable exists, run `agex version`, `agex doctor` (exit 1 "no agent is ready" is expected on runners) and `agex skills catalog`. No real agent ran on any runner.

## Release

| Item | Value |
| --- | --- |
| RELEASE | https://github.com/Abdullah-Dawoud/Agex/releases/tag/v2.0.0 |
| PUBLIC RELEASE TYPE | **Pre-release** |
| ARTIFACTS | `agex-2.0.0-win-x64.zip`, `agex-2.0.0-win-arm64.zip`, `agex-2.0.0-osx-arm64.zip`, `agex-2.0.0-osx-arm64.dmg`, `agex-2.0.0-osx-x64.zip`, `agex-2.0.0-osx-x64.dmg`, `agex-2.0.0-linux-x64.tar.gz`, `agex-2.0.0-linux-arm64.tar.gz`, `agex-install.ps1`, `agex-install.sh`, `skills-catalog.json`, `release-notes.md`, `SHA256SUMS.txt` |
| CHECKSUMS | PASS: all 12 files downloaded from the release and verified with `sha256sum -c SHA256SUMS.txt` |
| ONE-COMMAND INSTALL | VALIDATED: Windows one-liner run on the real Windows x64 machine (`-InstallDir` scratch folder, `-NoPath -NoShortcut -NoLaunch`): found v2.0.0, picked `win-x64`, verified checksum, installed, `agex version` = 2.0.0. The README one-liners also passed in CI on Windows x64/ARM64, macOS arm64/x64 and Linux x64/ARM64 (full install in a throw-away home folder, then `agex version`). |
| Update check | The released `agex update --check` finds v2.0.0 and reports "up to date". |
| Version | `VERSION`, assembly versions, package names, `Info.plist` and tag all use 2.0.0 |

Package naming kept the existing convention (`agex-<version>-<rid>`); there is no separate Windows setup `.exe`: the Windows installer is `agex-install.ps1`.

## Signing

| Item | Status |
| --- | --- |
| WINDOWS SIGNING | NOT CONFIGURED: `AgexDesktop.exe` and `agex.exe` are unsigned (`Get-AuthenticodeSignature`: NotSigned); SmartScreen will warn |
| MAC SIGNING | Ad-hoc only (needed for Apple Silicon to run it); Developer ID NOT CONFIGURED (no certificate chain in the signature) |
| MAC NOTARIZATION | NOT CONFIGURED: Gatekeeper asks users to allow AGEX once |
| AGEX RELEASE SIGNING KEY | NOT CONFIGURED: no `SHA256SUMS.txt.sig`; installers and updater verify SHA-256 only |

The workflow is ready for all four (secrets listed in `.github/workflows/release.yml` and `docs/DEVELOPMENT.md#signing`); nothing was faked.

## Known release limitations

- The desktop window has not been run on macOS, Linux or Windows ARM64; only the command line ran there (CI runners, no desktop session). Real-device checklists: `docs/MAC_TEST.md`, `docs/LINUX_TEST.md`.
- No real agent has run on macOS or Linux.
- Unsigned Windows binaries; macOS builds are ad-hoc signed and not notarized; checksums are not signed.
- Claude Code and Gemini CLI adapters remain beta and have not completed a request with a signed-in account.
- On Linux without a Secret Service, skill secrets are stored unencrypted in a user-only file (shown in Diagnostics).
- The code and installers use the old repository URL `Abdullah-Dawoud/Ai-COGY`, which works through GitHub's rename redirect; it stops working if a new repository takes that name.
- A test is intermittent on Windows: CI run 18 on `main` (commit `f3a509c`) failed in `test (windows-latest)`, while the same commit passed in CI run 19 and in release run 9 (which also runs the tests on Windows). Logs need sign-in, so the failing test was not identified; it is most likely the parallel engine test already listed as intermittent in `agex-product-maturity.md`.
- GitHub Actions warns that `actions/checkout@v4`, `setup-dotnet@v4`, `upload/download-artifact@v4` target the deprecated Node.js 20 (they still run on Node 24).
