# Contributing

Thanks for helping. Please:

1. Open an issue first for larger changes.
2. Keep changes focused. Build and run the tests described in [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) (.NET 10 SDK) before you open a pull request.
3. Add runtime dependencies only with a clear reason and a compatible open-source licence, and list them in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Never copy GPL or AGPL code.
4. Keep `Agex.Core` platform-independent; OS differences go behind `IPlatformService`.
5. Never commit secrets, tokens, logs, session files, machine paths or build output (`bin/`, `obj/`, `dist/`).
6. New agents come as adapters (see "Adding an agent adapter" in DEVELOPMENT.md). Do not add generic command execution.
7. Agent Room content must stay truthful: only explicit agent output and AGEX events, never hidden model reasoning.
8. Only mark a platform as tested in [reports/platform-compatibility.md](reports/platform-compatibility.md) if you ran AGEX on it.

By contributing you agree that your contribution is licensed under the MIT License.
