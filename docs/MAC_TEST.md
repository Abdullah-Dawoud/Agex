# Testing AGEX on a Mac

A short checklist for the first real-device test. Report results (macOS version, Mac model, chip, what failed) as a GitHub issue; attach **Settings → Diagnostics → Copy diagnostics**.

| Chip | Package |
| --- | --- |
| Apple Silicon (M1–M4): Apple menu → About This Mac shows "Chip Apple M…" | `agex-<version>-osx-arm64.zip` or `.dmg` |
| Intel: About This Mac shows "Processor … Intel" | `agex-<version>-osx-x64.zip` or `.dmg` |

The one-command installer picks the right one (`uname -m`: `arm64` or `x86_64`). An arm64 package does not run on Intel. An x64 package runs on Apple Silicon only through Rosetta 2, which is not what we want to test.

1. **Download** the package for your chip, or run the one-command install from the README.
2. **Open AGEX.** Builds are not notarized: the first time, right-click AGEX → Open → Open (or System Settings → Privacy & Security → Open Anyway). Note the exact message macOS shows.
3. **Complete onboarding** (six steps). Check that text, buttons and the theme look right, including dark mode (System Settings → Appearance).
4. **Detect an agent.** The Codex CLI or Antigravity CLI you installed should show **Ready**. If it shows Not installed, run `which codex` in Terminal and report the path: apps started from the Dock get a shorter PATH.
5. **Open a project.** Use a small test folder that is a Git repository.
6. **Run a simple request**, for example "Create hello.txt containing Hello". Allow the file change.
7. **Check the Agent Room**: assignment, result and tool events appear; Cmd+K opens the command palette.
8. **Quit and restart AGEX** (Cmd+Q).
9. **Confirm session history**: the request is listed under Sessions; **Undo changes** removes hello.txt.
10. **Update / uninstall**: `agex update --check` in Terminal, then `agex uninstall` (keeps data) or `agex uninstall --purge`.

Also note: the menu bar (File, View and Help menus; Cmd+N, Cmd+O, Cmd+Shift+L), a notification when a request finishes (allow notifications when asked), and whether VoiceOver (Cmd+F5) reads the main buttons.
