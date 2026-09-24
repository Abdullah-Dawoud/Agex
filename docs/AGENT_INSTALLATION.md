# Installing and signing in to agents

AGEX helps you install and sign in to the agents it supports, but the agents stay the vendors' own products: AGEX only uses each vendor's official distribution and each agent's own sign-in. It never sees or stores your passwords, and it never installs anything without your confirmation (never during the first-run scan).

Sources checked on 2026-09-24.

## What AGEX shows

Each agent card in **Agents** combines installation and sign-in:

| State | Meaning | Action offered |
| --- | --- | --- |
| Ready (`INSTALLED_READY`) | Installed, version check passed, signed in (or no account needed) | Test connection, Refresh models |
| Sign-in required (`INSTALLED_AUTH_REQUIRED`) | Installed, but the agent is not signed in | Sign in |
| Not working (`INSTALLED_BROKEN`) | Installed, version check failed | Retry detection |
| Not installed (`NOT_INSTALLED`) | Not found | Install (npm agents) or Install manually |
| Not available on this system (`PLATFORM_UNSUPPORTED`) | The vendor does not ship it for this OS | none |
| Detected - not integrated (`DETECTED_UNSUPPORTED`) | Found, but AGEX has no adapter | none |

**Install** always shows first: the agent, the source (npm package and publisher), what gets installed, the size where known, whether administrator rights are needed, and which company receives your data. It then runs exactly `npm install --global <package>` without a shell. If Node.js/npm is missing, AGEX offers the official Node.js download page instead.

**Sign in** opens a terminal window that runs the agent's own sign-in command. The agent opens its official browser sign-in and stores the credentials itself. AGEX then re-checks the sign-in state (automatically where that is quota-free) and shows **Signed in**.

**Test connection** runs the version check and the quota-free sign-in check again. It never sends a prompt to a model.

## Agents

### Codex CLI (OpenAI), stable

| | |
| --- | --- |
| Official source | https://github.com/openai/codex |
| Systems | Windows, macOS, Linux |
| Install | One click: `npm install -g @openai/codex` (official npm package). Manual alternatives from the vendor: `brew install --cask codex`, or the install script from chatgpt.com/codex |
| Sign in | `codex login` (browser sign-in with ChatGPT; `codex login --device-auth` for remote machines, run yourself) |
| Sign-in check | `codex login status` (no quota) |
| Model discovery | `codex debug models`: Codex's own catalog with names, context windows and reasoning levels; only models Codex lists for users are shown |
| Limitations | `codex debug` is marked as a debugging tool, so its format may change; AGEX then reports "could not read" and keeps the last good list. |

### Antigravity CLI (Google), stable

| | |
| --- | --- |
| Official source | https://antigravity.google/docs/cli/install/ |
| Systems | Windows, macOS, Linux |
| Install | Manual (Google's installer is a script): Windows PowerShell `irm https://antigravity.google/cli/install.ps1 \| iex`; macOS/Linux `curl -fsSL https://antigravity.google/cli/install.sh \| bash`. AGEX shows and copies the command; it does not run it. |
| Sign in | Run `agy`: it opens the browser for Google sign-in and keeps the sign-in in the system keychain |
| Sign-in check | `agy models` succeeds only when signed in (no model quota). Because it can start a sign-in by itself, AGEX runs it only when you press **Check sign-in**, **Test connection** or **Refresh models**, or after it succeeded before |
| Model discovery | `agy models` (id and display name) |
| Limitations | No context-window or price data is reported. |

### Claude Code (Anthropic), beta

| | |
| --- | --- |
| Official source | https://code.claude.com/docs/en/setup |
| Systems | Windows 10 1809+, macOS 13+, Ubuntu 20.04+, Debian 10+, Alpine 3.19+ |
| Install | One click: `npm install -g @anthropic-ai/claude-code` (official npm package; Anthropic recommends Node.js 22+ for this method). Anthropic's recommended native installer (`irm https://claude.ai/install.ps1 \| iex`, `curl -fsSL https://claude.ai/install.sh \| bash`), Homebrew and WinGet are shown as manual alternatives |
| Sign in | Run `claude` and follow the browser sign-in (or `/login`). Requires a Pro, Max, Team, Enterprise or Console account |
| Sign-in check | Claude Code has no status command. AGEX checks only that a sign-in marker exists, never its content: `ANTHROPIC_API_KEY` / `ANTHROPIC_AUTH_TOKEN` / `CLAUDE_CODE_OAUTH_TOKEN` in the environment, `~/.claude/.credentials.json` (Windows/Linux), or the account entry that accompanies a Keychain sign-in (macOS) |
| Model discovery | Not available (`MODEL_DISCOVERY_UNAVAILABLE`): Claude Code has no command that lists models. Use Auto, or a custom model ID under Advanced |
| Limitations | Not yet run with a signed-in account in AGEX's tests. |

### Gemini CLI (Google), beta

| | |
| --- | --- |
| Official source | https://github.com/google-gemini/gemini-cli |
| Systems | Windows, macOS, Linux |
| Install | One click: `npm install -g @google/gemini-cli`. Manual alternative: `brew install gemini-cli` |
| Sign in | Run `gemini` and choose **Login with Google**, or set `GEMINI_API_KEY` |
| Sign-in check | No status command. AGEX reads which sign-in method is selected in `~/.gemini/settings.json`, whether the matching key variable is set, and whether the Google sign-in file exists (never its contents) |
| Model discovery | Not available: Gemini CLI has no command that lists models |
| Limitations | Not yet run with a signed-in account in AGEX's tests. Vertex AI / Cloud Shell sign-ins cannot be checked without a request and show "Sign-in not checked". |

### Ollama, beta

| | |
| --- | --- |
| Official source | https://ollama.com/download |
| Systems | Windows, macOS, Linux |
| Install | Manual: the Windows and macOS apps from ollama.com/download; on Linux `curl -fsSL https://ollama.com/install.sh \| sh` (uses sudo) |
| Sign in | Not needed for local models |
| Model discovery | `GET /api/tags` on the local Ollama server: local models first, `...:cloud` models marked as running on Ollama's servers |
| Limitations | AGEX never starts Ollama or downloads models. Text answers only. |

## Models

- The model picker shows **Auto** (the agent's own default) plus the models the agent reports. Nothing is invented: size, context window or reasoning levels appear only when the agent reports them.
- Lists are cached for 12 hours in AGEX's cache folder and refreshed when an agent becomes ready, after you sign in, when you press **Refresh models**, and when the cache expires. A failed refresh (offline) keeps the last good list.
- If a model you picked is no longer offered, AGEX says "Previously selected model … is no longer available" and uses Auto.
- **Advanced → Custom model ID** accepts any ID; AGEX sends it as typed and never replaces it.
- Command line: `agex agents` (install and sign-in state) and `agex models [agent] [--refresh]`.
