# Testing AGEX on a Linux desktop

A short checklist for the first real-device test. Report results (distribution and version, desktop environment, X11 or Wayland, processor) as a GitHub issue; attach the output of `agex doctor`.

Package: `agex-<version>-linux-x64.tar.gz` (Intel/AMD) or `agex-<version>-linux-arm64.tar.gz` (ARM, `uname -m` shows `aarch64`).

1. **Install.** Run the one-command install from the README, or manually: `sh agex-install.sh --package ./agex-<version>-linux-x64.tar.gz`. Check that it reports "Checksum verified" and installs to `~/.local/share/agex/app` with `agex` in `~/.local/bin`.
2. **Desktop launch.** Start AGEX from the applications menu and with `agex` in a terminal. Note any missing library errors (for example `libICE`, `libSM`, `libfontconfig`).
3. **CLI.** `agex version`, `agex doctor`, `agex agents`.
4. **Agent detection.** An installed Codex CLI or Antigravity CLI shows **Ready**; Ollama shows Ready when its server runs.
5. **Project.** Open a small test folder that is a Git repository.
6. **Session.** Run "Create hello.txt containing Hello", allow the change, check the Agent Room and the file.
7. **Cancel.** Start a longer request ("Write a short README for this folder") and press **Cancel**. The agent processes must stop (`ps aux | grep -E 'codex|agy'`).
8. **History.** Restart AGEX; both requests are listed under Sessions; **Undo changes** removes hello.txt.
9. **Secrets.** Add the GitHub skill with a token: it needs a Secret Service (GNOME Keyring or KWallet with `secret-tool`). Without one, AGEX must refuse to store the token and say why.
10. **Uninstall.** `agex uninstall` removes the app, the `agex` link and the menu entry; `--purge` also removes `~/.local/share/agex`.

Also note: dark mode following the desktop theme, notifications (`notify-send`), and whether Orca reads the main buttons.
