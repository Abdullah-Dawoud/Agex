# Third-party notices

AGEX does not include third-party source code or bundled libraries.

At run time it uses components that are part of Windows (Windows PowerShell 5.1, .NET Framework 4.8 including WPF, `taskkill.exe`, `csc.exe` for building) and, when installed by the user, Git and the agent CLIs (Codex CLI, Antigravity CLI). These are not redistributed with AGEX and remain under their own licenses.

Techniques implemented from public documentation (not copied code): Windows command-line argument quoting rules (`CommandLineToArgvW`), `PeekNamedPipe` polling, bracketed paste mode.
