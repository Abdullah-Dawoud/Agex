# Troubleshooting

Start with **Diagnostics** in the app (or `agex doctor`). "What ran" shows each agent run: command, folder, exit code, time out or cancel, and the last lines of its error output. Logs are in `%LOCALAPPDATA%\AGEX\logs`.

| You see | Meaning | Do this |
| --- | --- | --- |
| Antigravity "Unavailable" | `agy.exe` is missing or did not answer `--version`. | AGEX runs with Codex only. Install or fix Antigravity, then Agents > Rescan. |
| "Antigravity is not signed in" | AGY started but has no login. | Open Antigravity, sign in, then Retry. |
| "Codex could not start planning. Trying Antigravity automatically..." | Codex failed before doing any work. | Nothing to do; AGEX retried once. Diagnostics shows the Codex error. |
| Could not start | No agent could plan the request. Nothing changed. | Read the reason; Retry, or change the leader on the Agents page. |
| Partial | Some tasks succeeded, some failed. | Tasks tab lists each failure and why. |
| "No agent can edit files right now" / task failed "read-only" | Antigravity is down and Codex is read-only. | Fix Antigravity, or allow Codex edits (Agents > Advanced). |
| An agent keeps being skipped | It failed to start and is paused for 5 minutes. | Retry, Test connection or Rescan. |
| "The AGEX engine stopped" banner | The engine process exited. | AGEX restarts it automatically; otherwise Diagnostics > Restart engine. |
| `agex` is not recognized | The command is not on PATH yet. | Open a new terminal; or run `agex repair` from the install folder (`%LOCALAPPDATA%\Programs\AGEX\bin\agex.cmd repair`). |
| "No AGEX release is published yet" | The update check found no release. | Nothing to do. |
| Terminal mode: Ctrl+Enter does nothing | Your terminal does not send Ctrl+Enter. | Use Ctrl+S, or type `:send` on its own line. |
| Terminal mode: screen garbled after resize | Rare redraw issue. | Press Ctrl+L. |

If AGEX was killed from outside (Task Manager), agent processes it started may still run. Stop them in Task Manager (`codex`, `node`, `agy`, `powershell` running `worker-run.ps1`).

Report a bug with the output of `agex doctor` and the matching log file. Logs are sanitized, but read them before sharing.
