# Contributing

Thanks for helping. Please:

1. Open an issue first for larger changes.
2. Keep changes focused. Build and run the tests in [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) before you open a pull request.
3. Do not add runtime dependencies. AGEX must run on stock Windows 10/11 (Windows PowerShell 5.1, .NET Framework 4.8).
4. Never commit secrets, tokens, logs, session files, machine paths or generated state (`dist/`, `AGEX.exe`).
5. New agents come as adapters (see "Adding an agent adapter"). Do not add generic command execution.
6. Agent Room content must stay truthful: only explicit agent output and AGEX events, never hidden model reasoning.

By contributing you agree that your contribution is licensed under the MIT License.
