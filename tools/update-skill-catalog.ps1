# Regenerates src/Agex.Core/Skills/skills-catalog.json (maintainers only).
#
#   powershell -File tools/update-skill-catalog.ps1 [-AsOf 2026-09-24]
#
# Clones the source repositories of the curated instruction skills (line endings
# untouched), pins each skill to the current commit and records the SHA-256 of
# every file. Review the catalog diff before committing: every change to a
# pinned commit changes what users install. MCP package versions, release dates
# and popularity numbers are edited by hand below (from npm/PyPI/GitHub on the
# -AsOf date). Cost labels (free, free-tier, paid; empty = derived: instruction
# skills are local, other tools free) come from each service's own pricing page
# on the -AsOf date. Selection criteria and the evidence for every entry:
# reports/skills-research.md.
param([string]$AsOf = (Get-Date -Format 'yyyy-MM-dd'))
$ErrorActionPreference = 'Stop'
$research = Join-Path ([IO.Path]::GetTempPath()) ('agex-skill-sources-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $research | Out-Null
foreach ($repo in 'obra/superpowers', 'anthropics/skills', 'openai/skills', 'JuliusBrussee/caveman', 'trailofbits/skills', 'github/awesome-copilot') {
    git -c core.autocrlf=false clone --depth 1 --quiet "https://github.com/$repo.git" (Join-Path $research ($repo -replace '/', '__'))
    if ($LASTEXITCODE -ne 0) { throw "Could not clone $repo." }
}
$out = Join-Path $PSScriptRoot '..\src\Agex.Core\Skills\skills-catalog.json'
$asOf = $AsOf
$all = @('windows', 'macos', 'linux')
$cli = @('codex', 'claude-code', 'antigravity', 'gemini-cli')

function Get-Files([string]$clone, [string]$base, [string[]]$exclude = @()) {
    $root = Join-Path $research "$clone\$($base -replace '/','\')"
    Get-ChildItem $root -Recurse -File | Sort-Object FullName | Where-Object {
        $rel = $_.FullName.Substring($root.Length + 1) -replace '\\', '/'
        -not ($exclude | Where-Object { $rel -like $_ })
    } | ForEach-Object {
        $rel = $_.FullName.Substring($root.Length + 1) -replace '\\', '/'
        [ordered]@{ path = $rel; sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(); size = $_.Length }
    }
}

function Instr([hashtable]$s) {
    $clone = $s.repo -replace '/', '__'
    $commit = (git -C (Join-Path $research $clone) rev-parse HEAD).Trim()
    $date = (git -C (Join-Path $research $clone) log -1 --format=%cs).Trim()
    $files = @(Get-Files $clone $s.base $s.exclude)
    $extra = @()
    if ($s.license_file) {
        $lf = Join-Path $research "$clone\$($s.license_file)"
        $extra += [ordered]@{ from = $s.license_file; to = 'LICENSE'; sha256 = (Get-FileHash $lf -Algorithm SHA256).Hash.ToLowerInvariant(); size = (Get-Item $lf).Length }
    }
    [ordered]@{
        id = $s.id; name = $s.name; kind = 'instructions'; description = $s.desc; author = $s.author; version = $commit.Substring(0, 7)
        license = $s.license; homepage = "https://github.com/$($s.repo)/tree/$commit/$($s.base)"; categories = $s.cat; recommended = [bool]$s.rec
        trust = $s.trust; capabilities = $s.caps; permissions = $s.perms; supported_agents = @(if ($s.agents) { $s.agents } else { $cli })
        supported_platforms = @(if ($s.platforms) { $s.platforms } else { $all }); required_tools = @($s.tools | Where-Object { $_ }); min_agex_version = '2.0.0'; compatibility_note = [string]$s.note
        popularity = [ordered]@{ label = "GitHub stars of $($s.repo) (whole repository)"; value = $s.stars; as_of = $asOf }
        release_notes = "Pinned to commit $($commit.Substring(0, 7)) of $($s.repo) ($date)."
        last_updated = $date; tags = @($s.tags | Where-Object { $_ }); risk_note = [string]$s.risk; cost = [string]$s.cost
        auth = $s.auth
        source = [ordered]@{ repository = $s.repo; commit = $commit; base_path = $s.base; files = $files; extra_files = $extra }
    }
}

function Mcp([hashtable]$s) {
    $agentNote = 'MCP servers are passed to Codex and Claude Code for each request. Antigravity and Gemini CLI read MCP servers only from their own settings, so AGEX does not pass it to them.'
    [ordered]@{
        id = $s.id; name = $s.name; kind = 'mcp'; description = $s.desc; author = $s.author; version = $s.version
        license = $s.license; homepage = $s.homepage; categories = $s.cat; recommended = [bool]$s.rec
        trust = $(if ($s.trust) { $s.trust } else { 'verified' }); capabilities = $s.caps; permissions = $s.perms; supported_agents = @('codex', 'claude-code')
        supported_platforms = @(if ($s.platforms) { $s.platforms } else { $all }); required_tools = @($s.tools | Where-Object { $_ }); min_agex_version = '2.0.0'; compatibility_note = $agentNote + $(if ($s.note) { ' ' + $s.note } else { '' })
        popularity = $(if ($s.pop) { [ordered]@{ label = $s.pop.label; value = $s.pop.value; as_of = $asOf } } else { $null })
        release_notes = $s.notes; last_updated = $s.date; tags = @($s.tags | Where-Object { $_ }); risk_note = [string]$s.risk; cost = [string]$s.cost
        auth = $s.auth
        mcp = $s.mcp
    }
}

function Key([string]$secret, [string]$label, [string]$setup, $test = $null, [string]$note = '') {
    [ordered]@{ type = 'api_key'; secret = $secret; label = $label; setup_url = $setup; note = $note; test = $test }
}
function CliLogin([string]$tool, [string[]]$loginArgs, [string]$label, [string]$setup) {
    [ordered]@{ type = 'cli_login'; login_tool = $tool; login_args = $loginArgs; label = $label; setup_url = $setup }
}

$sp = @{ repo = 'obra/superpowers'; license = 'MIT'; license_file = 'LICENSE'; author = 'Jesse Vincent (Superpowers)'; trust = 'curated'; stars = 291063 }
$cm = @{ repo = 'JuliusBrussee/caveman'; license = 'MIT (skills folder; engine and proxy are BSL-1.1 and not included)'; license_file = 'LICENSE'; author = 'Julius Brussee (Caveman)'; trust = 'curated'; stars = 107660 }
$an = @{ repo = 'anthropics/skills'; license = 'Apache-2.0'; author = 'Anthropic'; trust = 'verified'; stars = 177925 }
$oa = @{ repo = 'openai/skills'; exclude = @('agents/*', 'assets/*'); license = 'Apache-2.0'; author = 'OpenAI'; trust = 'verified'; stars = 27608 }
$tob = @{ repo = 'trailofbits/skills'; license = 'CC-BY-SA-4.0'; license_file = 'LICENSE'; author = 'Trail of Bits'; trust = 'curated'; stars = 7226 }
$gc = @{ repo = 'github/awesome-copilot'; license = 'MIT'; license_file = 'LICENSE'; author = 'GitHub Awesome Copilot contributors'; trust = 'community'; stars = 39352 }
function With([hashtable]$base, [hashtable]$extra) { $h = $base.Clone(); foreach ($k in $extra.Keys) { $h[$k] = $extra[$k] }; $h }

$skills = @(
    # ------------------------------------------------ Superpowers (MIT)
    (Instr (With $sp @{ id = 'systematic-debugging'; name = 'Systematic Debugging'; base = 'skills/systematic-debugging'; exclude = @('test-*.md', 'CREATION-LOG.md')
        desc = 'Find the root cause before fixing: reproduce, trace, form one hypothesis at a time, and prove the fix.'; cat = @('Developer', 'Debugging'); rec = $true; caps = @('debugging'); perms = @('read_files', 'run_commands'); tags = @('popular') })),
    (Instr (With $sp @{ id = 'test-driven-development'; name = 'Test-Driven Development'; base = 'skills/test-driven-development'
        desc = 'Write a failing test first, make it pass, then clean up. Keeps changes small and proven.'; cat = @('Developer', 'Testing'); rec = $true; caps = @('testing'); perms = @('read_files', 'write_files', 'run_commands'); tags = @('popular') })),
    (Instr (With $sp @{ id = 'verification-before-completion'; name = 'Verify Before Done'; base = 'skills/verification-before-completion'
        desc = 'Agents must run the checks and show the evidence before they say work is finished.'; cat = @('Developer', 'Productivity'); caps = @('testing', 'code_review'); perms = @('read_files', 'run_commands'); tags = @('popular') })),
    (Instr (With $sp @{ id = 'writing-plans'; name = 'Writing Plans'; base = 'skills/writing-plans'
        desc = 'Turn a request into a step-by-step implementation plan with files, tests and checkpoints.'; cat = @('Developer', 'Productivity'); caps = @('planning'); perms = @('read_files', 'write_files'); tags = @('popular') })),
    (Instr (With $sp @{ id = 'executing-plans'; name = 'Executing Plans'; base = 'skills/executing-plans'
        desc = 'Work through a written plan task by task, checking each step before moving on.'; cat = @('Developer', 'Productivity'); caps = @('planning'); perms = @('read_files', 'write_files', 'run_commands', 'git'); tools = @('git'); tags = @('popular') })),
    (Instr (With $sp @{ id = 'requesting-code-review'; name = 'Code Review'; base = 'skills/requesting-code-review'
        desc = 'A structured review of finished work against its requirements, with issues ranked by severity.'; cat = @('Developer', 'Recommended'); rec = $true; caps = @('code_review'); perms = @('read_files', 'run_commands'); tags = @('popular')
        note = 'Written for agents that can start a separate reviewer; other agents follow the same checklist themselves.' })),
    (Instr (With $sp @{ id = 'receiving-code-review'; name = 'Receiving Code Review'; base = 'skills/receiving-code-review'
        desc = 'Handle review feedback carefully: verify each suggestion before changing code, push back when it is wrong.'; cat = @('Developer', 'Git & GitHub'); caps = @('code_review'); perms = @('read_files', 'write_files'); tags = @('popular') })),
    (Instr (With $sp @{ id = 'finishing-a-development-branch'; name = 'Finish a Branch'; base = 'skills/finishing-a-development-branch'
        desc = 'When work is done and tests pass: choose how to integrate the branch (merge, pull request or cleanup) safely.'; cat = @('Developer', 'Git & GitHub'); caps = @('planning'); perms = @('read_files', 'run_commands', 'git'); tools = @('git'); tags = @('popular') })),
    (Instr (With $sp @{ id = 'using-git-worktrees'; name = 'Git Worktrees'; base = 'skills/using-git-worktrees'
        desc = 'Start feature work in a separate Git worktree so the main checkout stays untouched.'; cat = @('Developer', 'Git & GitHub'); caps = @('planning'); perms = @('read_files', 'write_files', 'run_commands', 'git'); tools = @('git'); tags = @('popular', 'advanced') })),

    # ------------------------------------------------ Caveman (MIT skills)
    (Instr (With $cm @{ id = 'caveman'; name = 'Caveman (short answers)'; base = 'skills/caveman'
        desc = 'Agents answer in compressed, fragment-style text that keeps technical detail: fewer output tokens, faster reading.'; cat = @('Productivity'); caps = @('documents'); perms = @(); tags = @('popular')
        note = 'Changes how agents write, not what they do. Its rules add about 1,000-1,500 input tokens per turn, so savings depend on how long the answers would have been.' })),
    (Instr (With $cm @{ id = 'caveman-commit'; name = 'Caveman Commit Messages'; base = 'skills/caveman-commit'
        desc = 'Short, precise commit messages in Conventional Commits style.'; cat = @('Git & GitHub', 'Productivity'); caps = @('documents'); perms = @('read_files', 'git'); tools = @('git'); tags = @('popular') })),
    (Instr (With $cm @{ id = 'caveman-review'; name = 'Caveman Code Review'; base = 'skills/caveman-review'
        desc = 'One-line review comments: location, problem, fix. No filler.'; cat = @('Developer'); caps = @('code_review'); perms = @('read_files'); tags = @('popular') })),

    # ------------------------------------------------ Anthropic (Apache-2.0 per skill)
    (Instr (With $an @{ id = 'webapp-testing'; name = 'Web App Testing'; base = 'skills/webapp-testing'
        desc = 'Test a local web app with Playwright for Python: check behaviour, capture screenshots and console logs.'; cat = @('Developer', 'Testing', 'Web'); caps = @('testing', 'browser'); perms = @('read_files', 'run_commands', 'browser'); tools = @('python'); tags = @('popular') })),
    (Instr (With $an @{ id = 'frontend-design'; name = 'Frontend Design'; base = 'skills/frontend-design'
        desc = 'Guidance for deliberate visual design of user interfaces: layout, typography and a distinct look.'; cat = @('Design', 'Web'); caps = @('documents'); perms = @('read_files', 'write_files'); tags = @('popular') })),
    (Instr (With $an @{ id = 'mcp-builder'; name = 'MCP Server Builder'; base = 'skills/mcp-builder'
        desc = 'Guide for designing, building and evaluating your own MCP servers in Python or TypeScript.'; cat = @('Developer'); caps = @('planning'); perms = @('read_files', 'write_files', 'run_commands'); tools = @('python'); tags = @('advanced') })),
    (Instr (With $an @{ id = 'skill-creator'; name = 'Skill Creator'; base = 'skills/skill-creator'
        desc = 'Create and improve your own SKILL.md skills, with scripts to test how well they trigger.'; cat = @('Productivity'); caps = @('documents'); perms = @('read_files', 'write_files', 'run_commands'); tools = @('python'); tags = @('advanced') })),

    # ------------------------------------------------ OpenAI curated (Apache-2.0; vercel-deploy MIT by Vercel)
    (Instr (With $oa @{ id = 'security-best-practices'; name = 'Security Best Practices'; base = 'skills/.curated/security-best-practices'; exclude = @('agents/*')
        desc = 'Language- and framework-specific security review (Go, JavaScript, Python web stacks) with concrete fixes.'; cat = @('Developer', 'Security'); caps = @('code_review'); perms = @('read_files') })),
    (Instr (With $oa @{ id = 'security-threat-model'; name = 'Threat Model'; base = 'skills/.curated/security-threat-model'; exclude = @('agents/*')
        desc = 'Writes a threat model for the project: trust boundaries, assets, attack paths and mitigations.'; cat = @('Developer', 'Security'); caps = @('code_review', 'documents'); perms = @('read_files', 'write_files') })),
    (Instr (With $oa @{ id = 'security-ownership-map'; name = 'Security Ownership Map'; base = 'skills/.curated/security-ownership-map'
        desc = 'Maps who knows which code from Git history: bus factor and security-sensitive files without an owner.'; cat = @('Security', 'Git & GitHub'); caps = @('code_review'); perms = @('read_files', 'run_commands', 'git'); tools = @('python', 'git'); tags = @('advanced') })),
    (Instr (With $oa @{ id = 'gh-fix-ci'; name = 'Fix GitHub CI'; base = 'skills/.curated/gh-fix-ci'
        desc = 'Reads failing GitHub Actions checks on a pull request, explains the failure and proposes a fix.'; cat = @('Developer', 'DevOps', 'Git & GitHub'); caps = @('debugging'); perms = @('read_files', 'write_files', 'run_commands', 'network', 'github'); tools = @('gh', 'python')
        auth = (CliLogin 'gh' @('auth', 'login') 'Sign in with the GitHub CLI' 'https://cli.github.com/manual/gh_auth_login') })),
    (Instr (With $oa @{ id = 'gh-address-comments'; name = 'Address PR Comments'; base = 'skills/.curated/gh-address-comments'
        desc = 'Collects review comments on the current pull request and works through them.'; cat = @('Developer', 'Git & GitHub'); caps = @('code_review'); perms = @('read_files', 'write_files', 'run_commands', 'network', 'github'); tools = @('gh', 'python')
        auth = (CliLogin 'gh' @('auth', 'login') 'Sign in with the GitHub CLI' 'https://cli.github.com/manual/gh_auth_login') })),
    (Instr (With $oa @{ id = 'playwright-cli'; name = 'Browser Automation (Playwright CLI)'; base = 'skills/.curated/playwright'
        desc = 'Drive a real browser from the terminal: open pages, fill forms, take screenshots, extract data.'; cat = @('Web', 'Testing'); caps = @('browser', 'testing'); perms = @('run_commands', 'network', 'browser'); tools = @('node') })),
    (Instr (With $oa @{ id = 'pdf-documents'; name = 'PDF Documents'; base = 'skills/.curated/pdf'
        desc = 'Read, create and check PDF files, including visual checks of rendered pages.'; cat = @('Documents'); caps = @('documents'); perms = @('read_files', 'write_files', 'run_commands'); tools = @('python') })),
    (Instr (With $oa @{ id = 'jupyter-notebook'; name = 'Jupyter Notebooks'; base = 'skills/.curated/jupyter-notebook'; exclude = @('agents/*', 'assets/*.png', 'assets/*.svg')
        desc = 'Create clean, well-structured notebooks for experiments and tutorials.'; cat = @('Data'); caps = @('documents'); perms = @('read_files', 'write_files', 'run_commands'); tools = @('python') })),
    (Instr (With $oa @{ id = 'vercel-deploy'; name = 'Deploy to Vercel'; base = 'skills/.curated/vercel-deploy'; license = 'MIT'; author = 'Vercel (in OpenAI skills)'
        desc = 'Deploy a web app or site to Vercel with the Vercel CLI and report the preview link.'; cat = @('DevOps', 'Web'); caps = @('planning'); perms = @('read_files', 'run_commands', 'network'); tools = @('vercel')
        auth = (CliLogin 'vercel' @('login') 'Sign in with the Vercel CLI' 'https://vercel.com/docs/cli/login') })),
    (Instr (With $oa @{ id = 'netlify-deploy'; name = 'Deploy to Netlify'; base = 'skills/.curated/netlify-deploy'
        desc = 'Deploy a web project to Netlify with the Netlify CLI (through npx).'; cat = @('DevOps', 'Web'); caps = @('planning'); perms = @('read_files', 'write_files', 'run_commands', 'network'); tools = @('node')
        auth = (CliLogin 'node' @('netlify', 'login') 'Sign in with the Netlify CLI' 'https://docs.netlify.com/cli/get-started/#authentication') })),
    (Instr (With $oa @{ id = 'cloudflare-deploy'; name = 'Deploy to Cloudflare'; base = 'skills/.curated/cloudflare-deploy'
        desc = 'Deploy apps to Cloudflare Workers and Pages with Wrangler, with reference material for Cloudflare products.'; cat = @('DevOps', 'Web'); caps = @('planning'); perms = @('read_files', 'write_files', 'run_commands', 'network'); tools = @('node')
        auth = (CliLogin 'node' @('wrangler', 'login') 'Sign in with Cloudflare Wrangler' 'https://developers.cloudflare.com/workers/wrangler/commands/#login') })),
    (Instr (With $oa @{ id = 'render-deploy'; name = 'Deploy to Render'; base = 'skills/.curated/render-deploy'
        desc = 'Deploy to Render: analyse the project, write a render.yaml Blueprint and create the services.'; cat = @('DevOps', 'Web'); caps = @('planning'); perms = @('read_files', 'write_files', 'run_commands', 'network'); tools = @('render')
        auth = (CliLogin 'render' @('login') 'Sign in with the Render CLI' 'https://render.com/docs/cli#authenticate') })),
    (Instr (With $oa @{ id = 'aspnet-core'; name = 'ASP.NET Core'; base = 'skills/.curated/aspnet-core'
        desc = 'Build, review and refactor ASP.NET Core web apps using current official guidance.'; cat = @('Developer', 'Web'); caps = @('code_review', 'planning'); perms = @('read_files', 'write_files', 'run_commands'); tools = @('dotnet') })),
    (Instr (With $oa @{ id = 'winui-app'; name = 'WinUI 3 Apps'; base = 'skills/.curated/winui-app'
        desc = 'Create and develop modern WinUI 3 desktop apps with C# and the Windows App SDK.'; cat = @('Developer', 'Design'); caps = @('planning'); perms = @('read_files', 'write_files', 'run_commands'); tools = @('dotnet'); platforms = @('windows') })),

    # ------------------------------------------------ Trail of Bits (CC-BY-SA-4.0)
    (Instr (With $tob @{ id = 'differential-review'; name = 'Security Diff Review'; base = 'plugins/differential-review/skills/differential-review'
        desc = 'Security-focused review of a change set, scaled to the size of the codebase and the risk of the change.'; cat = @('Security', 'Git & GitHub'); caps = @('code_review'); perms = @('read_files', 'git'); tools = @('git') })),
    (Instr (With $tob @{ id = 'property-based-testing'; name = 'Property-Based Testing'; base = 'plugins/property-based-testing/skills/property-based-testing'
        desc = 'Write and debug property-based tests (Hypothesis, fast-check, proptest and others) that find edge cases.'; cat = @('Developer', 'Testing'); caps = @('testing'); perms = @('read_files', 'write_files', 'run_commands') })),
    (Instr (With $tob @{ id = 'modern-python'; name = 'Modern Python Tooling'; base = 'plugins/modern-python/skills/modern-python'
        desc = 'Set up Python projects with uv, ruff and type checking; standalone scripts with inline dependencies.'; cat = @('Developer'); caps = @('planning'); perms = @('read_files', 'write_files', 'run_commands'); tools = @('uv') })),
    (Instr (With $tob @{ id = 'semgrep-scan'; name = 'Semgrep Static Analysis'; base = 'plugins/static-analysis/skills/semgrep'
        desc = 'Scan code with Semgrep, triage the findings and separate real issues from noise.'; cat = @('Security'); caps = @('code_review'); perms = @('read_files', 'run_commands'); tools = @('semgrep'); tags = @('advanced') })),

    # ------------------------------------------------ GitHub Awesome Copilot (MIT, community contributions)
    (Instr (With $gc @{ id = 'acquire-codebase-knowledge'; name = 'Codebase Onboarding Map'; base = 'skills/acquire-codebase-knowledge'
        desc = 'Map an unfamiliar codebase: structure, entry points, conventions and where to start, written to a document.'; cat = @('Developer', 'Research'); caps = @('documents'); perms = @('read_files', 'write_files', 'run_commands'); tools = @('python')
        risk = 'Community contribution: it runs its own Python scan script.' })),
    (Instr (With $gc @{ id = 'codeql-setup'; name = 'CodeQL Setup'; base = 'skills/codeql'
        desc = 'Set up CodeQL code scanning with GitHub Actions or the CodeQL CLI.'; cat = @('Security', 'DevOps'); caps = @('code_review'); perms = @('read_files', 'write_files'); tags = @('advanced')
        risk = 'Community contribution.' })),

    # ------------------------------------------------ MCP tools
    (Mcp @{ id = 'playwright-mcp'; name = 'Browser (Playwright MCP)'; version = '0.0.82'; license = 'Apache-2.0'; author = 'Microsoft'; homepage = 'https://github.com/microsoft/playwright-mcp'; date = '2026-09-18'
        desc = 'Lets agents open web pages, click, type and read pages in a real browser.'; cat = @('Recommended', 'Web', 'Testing'); rec = $true; caps = @('browser'); perms = @('network', 'browser', 'run_commands', 'mcp'); tools = @('node'); tags = @('popular')
        pop = @{ label = 'npm downloads of @playwright/mcp, last month'; value = 23800747 }; notes = 'Pinned npm version 0.0.82 (published 2026-09-18).'
        mcp = [ordered]@{ transport = 'stdio'; command = 'npx'; args = @('-y', '@playwright/mcp@0.0.82'); secret_env = @() } }),
    (Mcp @{ id = 'chrome-devtools-mcp'; name = 'Chrome DevTools'; version = '1.10.1'; license = 'Apache-2.0'; author = 'Google Chrome DevTools team'; homepage = 'https://github.com/ChromeDevTools/chrome-devtools-mcp'; date = '2026-09-23'
        desc = 'Lets agents inspect a Chrome page: console, network requests and performance traces.'; cat = @('Web', 'Debugging'); caps = @('browser', 'debugging'); perms = @('network', 'browser', 'run_commands', 'mcp'); tools = @('node', 'chrome'); tags = @('popular')
        pop = @{ label = 'npm downloads of chrome-devtools-mcp, last month'; value = 7881886 }; notes = 'Pinned npm version 1.10.1 (published 2026-09-23).'
        mcp = [ordered]@{ transport = 'stdio'; command = 'npx'; args = @('-y', 'chrome-devtools-mcp@1.10.1'); secret_env = @() } }),
    (Mcp @{ id = 'context7'; cost = 'free-tier'; name = 'Library Docs (Context7)'; version = '4.1.1'; license = 'MIT'; author = 'Upstash'; homepage = 'https://github.com/upstash/context7'; date = '2026-09-14'
        desc = 'Gives agents current documentation for programming libraries instead of outdated memory.'; cat = @('Recommended', 'Research', 'Developer'); rec = $true; caps = @('web_research'); perms = @('network', 'run_commands', 'mcp'); tools = @('node'); tags = @('popular')
        pop = @{ label = 'npm downloads of @upstash/context7-mcp, last month'; value = 3451044 }; notes = 'Pinned npm version 4.1.1 (published 2026-09-14). Library names in your requests are sent to the Context7 service.'
        mcp = [ordered]@{ transport = 'stdio'; command = 'npx'; args = @('-y', '@upstash/context7-mcp@4.1.1'); secret_env = @() } }),
    (Mcp @{ id = 'web-fetch'; name = 'Web Research (Fetch)'; version = '2026.8.18'; license = 'MIT'; author = 'Model Context Protocol project'; homepage = 'https://github.com/modelcontextprotocol/servers/tree/main/src/fetch'; date = '2026-08-18'
        desc = 'Lets agents download a web page and read it as text.'; cat = @('Recommended', 'Research', 'Web'); rec = $true; caps = @('web_research'); perms = @('network', 'run_commands', 'mcp'); tools = @('uv')
        pop = $null; notes = 'Pinned PyPI version 2026.8.18.'; note = 'It can reach local network addresses; use it only with sites you trust.'; risk = 'Can reach addresses on your local network.'
        mcp = [ordered]@{ transport = 'stdio'; command = 'uvx'; args = @('mcp-server-fetch==2026.8.18'); secret_env = @() } }),
    (Mcp @{ id = 'github-mcp'; cost = 'free'; name = 'Git & GitHub'; version = 'remote'; license = 'MIT'; author = 'GitHub'; homepage = 'https://github.com/github/github-mcp-server'; date = '2026-09-22'
        desc = 'Lets agents read and manage GitHub issues, pull requests, reviews and Actions with your token.'; cat = @('Recommended', 'Git & GitHub', 'DevOps'); rec = $true; caps = @('mcp'); perms = @('network', 'github', 'mcp'); tools = @()
        pop = $null; notes = 'Uses GitHub''s hosted MCP endpoint. Your token is stored in the system keychain and passed by environment variable, never on a command line.'
        auth = (Key 'GITHUB_PERSONAL_ACCESS_TOKEN' 'GitHub personal access token' 'https://github.com/settings/personal-access-tokens' ([ordered]@{ url = 'https://api.github.com/user'; header = 'Authorization'; scheme = 'Bearer '; extra_headers = [ordered]@{ Accept = 'application/vnd.github+json' }; identity_field = 'login' }) 'A fine-grained token limited to the repositories you want agents to use is safest.')
        mcp = [ordered]@{ transport = 'http'; url = 'https://api.githubcopilot.com/mcp/'; bearer_secret = 'GITHUB_PERSONAL_ACCESS_TOKEN'; secret_env = @('GITHUB_PERSONAL_ACCESS_TOKEN') } }),
    (Mcp @{ id = 'exa-search'; cost = 'free-tier'; name = 'Web Search (Exa)'; version = 'remote'; license = 'MIT'; author = 'Exa Labs'; homepage = 'https://github.com/exa-labs/exa-mcp-server'; date = '2026-08-18'
        desc = 'Web search and page reading built for agents. Works without an account (rate-limited).'; cat = @('Research', 'Web'); caps = @('web_research'); perms = @('network', 'mcp'); tools = @()
        pop = @{ label = 'npm downloads of exa-mcp-server, last month'; value = 248026 }; notes = 'Uses Exa''s hosted MCP endpoint anonymously. Search queries go to Exa.'
        mcp = [ordered]@{ transport = 'http'; url = 'https://mcp.exa.ai/mcp'; secret_env = @() } }),
    (Mcp @{ id = 'brave-search'; cost = 'free-tier'; name = 'Web Search (Brave)'; version = '2.1.4'; license = 'MIT'; author = 'Brave Software'; homepage = 'https://github.com/brave/brave-search-mcp-server'; date = '2026-09-17'
        desc = 'Web, news and image search through the Brave Search API.'; cat = @('Research', 'Web'); caps = @('web_research'); perms = @('network', 'run_commands', 'mcp'); tools = @('node')
        pop = @{ label = 'npm downloads of @brave/brave-search-mcp-server, last month'; value = 58065 }; notes = 'Pinned npm version 2.1.4 (published 2026-09-17). Search queries go to Brave.'
        auth = (Key 'BRAVE_API_KEY' 'Brave Search API key' 'https://brave.com/search/api/' $null 'A free plan is available. AGEX does not test the key, because every test would use one search from your quota.')
        mcp = [ordered]@{ transport = 'stdio'; command = 'npx'; args = @('-y', '@brave/brave-search-mcp-server@2.1.4'); secret_env = @('BRAVE_API_KEY') } }),
    (Mcp @{ id = 'tavily-search'; cost = 'free-tier'; name = 'Web Research (Tavily)'; version = '0.2.22'; license = 'MIT'; author = 'Tavily'; homepage = 'https://github.com/tavily-ai/tavily-mcp'; date = '2026-08-05'
        desc = 'Search, extract and crawl the web with Tavily''s research API.'; cat = @('Research', 'Web'); caps = @('web_research'); perms = @('network', 'run_commands', 'mcp'); tools = @('node')
        pop = @{ label = 'npm downloads of tavily-mcp, last month'; value = 77144 }; notes = 'Pinned npm version 0.2.22 (published 2026-08-05). Queries go to Tavily.'
        auth = (Key 'TAVILY_API_KEY' 'Tavily API key' 'https://app.tavily.com/home' $null 'A free plan is available.')
        mcp = [ordered]@{ transport = 'stdio'; command = 'npx'; args = @('-y', 'tavily-mcp@0.2.22'); secret_env = @('TAVILY_API_KEY') } }),
    (Mcp @{ id = 'firecrawl'; cost = 'free-tier'; name = 'Web Scraping (Firecrawl)'; version = '3.25.4'; license = 'MIT'; author = 'Firecrawl'; homepage = 'https://github.com/firecrawl/firecrawl-mcp-server'; date = '2026-09-23'
        desc = 'Turn web pages and whole sites into clean Markdown or structured data.'; cat = @('Research', 'Web', 'Data'); caps = @('web_research'); perms = @('network', 'run_commands', 'mcp'); tools = @('node')
        pop = @{ label = 'npm downloads of firecrawl-mcp, last month'; value = 126701 }; notes = 'Pinned npm version 3.25.4 (published 2026-09-23). Pages you ask for are fetched by Firecrawl.'
        auth = (Key 'FIRECRAWL_API_KEY' 'Firecrawl API key' 'https://www.firecrawl.dev/app/api-keys' $null 'Free credits are available.')
        mcp = [ordered]@{ transport = 'stdio'; command = 'npx'; args = @('-y', 'firecrawl-mcp@3.25.4'); secret_env = @('FIRECRAWL_API_KEY') } }),
    (Mcp @{ id = 'microsoft-learn'; cost = 'free'; name = 'Microsoft Learn Docs'; version = 'remote'; license = 'Hosted service (Microsoft terms)'; author = 'Microsoft'; homepage = 'https://learn.microsoft.com/training/support/mcp'; date = $asOf
        desc = 'Search and read official Microsoft documentation (.NET, Azure, Windows, Microsoft 365). No account needed.'; cat = @('Research', 'Developer'); caps = @('web_research'); perms = @('network', 'mcp'); tools = @()
        pop = $null; notes = 'Microsoft''s hosted MCP endpoint. Search queries go to Microsoft.'
        mcp = [ordered]@{ transport = 'http'; url = 'https://learn.microsoft.com/api/mcp'; secret_env = @() } }),
    (Mcp @{ id = 'aws-docs'; name = 'AWS Documentation'; version = '1.2.1'; license = 'Apache-2.0'; author = 'AWS Labs'; homepage = 'https://github.com/awslabs/mcp/tree/main/src/aws-documentation-mcp-server'; date = '2026-09-08'
        desc = 'Search and read official AWS documentation. No AWS account needed.'; cat = @('Research', 'DevOps'); caps = @('web_research'); perms = @('network', 'run_commands', 'mcp'); tools = @('uv')
        pop = $null; notes = 'Pinned PyPI version 1.2.1 (published 2026-09-08).'
        mcp = [ordered]@{ transport = 'stdio'; command = 'uvx'; args = @('awslabs.aws-documentation-mcp-server==1.2.1'); secret_env = @() } }),
    (Mcp @{ id = 'cloudflare-docs'; cost = 'free'; name = 'Cloudflare Docs'; version = 'remote'; license = 'Hosted service (Cloudflare terms)'; author = 'Cloudflare'; homepage = 'https://github.com/cloudflare/mcp-server-cloudflare'; date = $asOf
        desc = 'Search Cloudflare''s developer documentation (Workers, Pages, R2, D1 and more). No account needed.'; cat = @('Research', 'DevOps'); caps = @('web_research'); perms = @('network', 'mcp'); tools = @()
        pop = $null; notes = 'Cloudflare''s hosted documentation MCP endpoint.'
        mcp = [ordered]@{ transport = 'http'; url = 'https://docs.mcp.cloudflare.com/mcp'; secret_env = @() } }),
    (Mcp @{ id = 'deepwiki'; cost = 'free'; name = 'Repository Wiki (DeepWiki)'; version = 'remote'; license = 'Hosted service (Cognition terms)'; author = 'Cognition'; homepage = 'https://docs.devin.ai/work-with-devin/deepwiki-mcp'; date = $asOf
        desc = 'Ask questions about public GitHub repositories and read their generated documentation.'; cat = @('Research', 'Developer'); caps = @('web_research'); perms = @('network', 'mcp'); tools = @()
        pop = $null; notes = 'Cognition''s hosted MCP endpoint; public repositories only. Repository names and questions go to Cognition.'
        mcp = [ordered]@{ transport = 'http'; url = 'https://mcp.deepwiki.com/mcp'; secret_env = @() } }),
    (Mcp @{ id = 'notion-mcp'; cost = 'free-tier'; name = 'Notion'; version = '2.5.2'; license = 'MIT'; author = 'Notion'; homepage = 'https://github.com/makenotion/notion-mcp-server'; date = '2026-09-20'
        desc = 'Read and update Notion pages and databases that you shared with an integration.'; cat = @('Productivity', 'Documents'); caps = @('documents'); perms = @('network', 'run_commands', 'mcp'); tools = @('node')
        pop = @{ label = 'npm downloads of @notionhq/notion-mcp-server, last month'; value = 634404 }; notes = 'Pinned npm version 2.5.2 (published 2026-09-20).'
        auth = (Key 'NOTION_TOKEN' 'Notion integration token' 'https://www.notion.so/profile/integrations' ([ordered]@{ url = 'https://api.notion.com/v1/users/me'; header = 'Authorization'; scheme = 'Bearer '; extra_headers = [ordered]@{ 'Notion-Version' = '2022-06-28' }; identity_field = 'name' }) 'Create an internal integration, then share only the pages agents may use with it.')
        mcp = [ordered]@{ transport = 'stdio'; command = 'npx'; args = @('-y', '@notionhq/notion-mcp-server@2.5.2'); secret_env = @('NOTION_TOKEN') } }),
    (Mcp @{ id = 'linear-mcp'; cost = 'free-tier'; name = 'Linear'; version = 'remote'; license = 'Hosted service (Linear terms)'; author = 'Linear'; homepage = 'https://linear.app/docs/mcp'; date = $asOf
        desc = 'Find, create and update Linear issues, projects and comments.'; cat = @('Productivity', 'Git & GitHub'); caps = @('mcp'); perms = @('network', 'mcp'); tools = @()
        pop = $null; notes = 'Linear''s hosted MCP endpoint with your API key as a bearer token.'
        auth = (Key 'LINEAR_API_KEY' 'Linear personal API key' 'https://linear.app/settings/account/security' $null 'Linear shows the key once when you create it.')
        mcp = [ordered]@{ transport = 'http'; url = 'https://mcp.linear.app/mcp'; bearer_secret = 'LINEAR_API_KEY'; secret_env = @('LINEAR_API_KEY') } }),
    (Mcp @{ id = 'sentry-mcp'; cost = 'free-tier'; name = 'Sentry'; version = '0.40.0'; license = 'FSL-1.1-ALv2 (source-available; becomes Apache-2.0 after two years)'; author = 'Sentry'; homepage = 'https://github.com/getsentry/sentry-mcp'; date = '2026-09-24'
        desc = 'Look up Sentry issues, errors and traces so agents can debug production problems.'; cat = @('Debugging', 'DevOps'); caps = @('debugging'); perms = @('network', 'run_commands', 'mcp'); tools = @('node')
        pop = @{ label = 'npm downloads of @sentry/mcp-server, last month'; value = 417238 }; notes = 'Pinned npm version 0.40.0 (published 2026-09-24).'
        auth = (Key 'SENTRY_ACCESS_TOKEN' 'Sentry user auth token' 'https://sentry.io/settings/account/api/auth-tokens/' $null 'Give the token only the read scopes you need.')
        mcp = [ordered]@{ transport = 'stdio'; command = 'npx'; args = @('-y', '@sentry/mcp-server@0.40.0'); secret_env = @('SENTRY_ACCESS_TOKEN') } }),
    (Mcp @{ id = 'supabase-mcp'; cost = 'free-tier'; name = 'Supabase (read-only)'; version = '0.13.0'; license = 'Apache-2.0'; author = 'Supabase'; homepage = 'https://github.com/supabase/mcp'; date = '2026-09-17'
        desc = 'Inspect your Supabase projects: tables, schema, logs and docs. Started in read-only mode.'; cat = @('Data', 'DevOps'); caps = @('mcp'); perms = @('network', 'run_commands', 'mcp'); tools = @('node')
        pop = @{ label = 'npm downloads of @supabase/mcp-server-supabase, last month'; value = 406869 }; notes = 'Pinned npm version 0.13.0 (published 2026-09-17), started with --read-only.'
        risk = 'Connects to your databases. AGEX starts it read-only; use a development project where possible.'
        auth = (Key 'SUPABASE_ACCESS_TOKEN' 'Supabase personal access token' 'https://supabase.com/dashboard/account/tokens' ([ordered]@{ url = 'https://api.supabase.com/v1/projects'; header = 'Authorization'; scheme = 'Bearer '; extra_headers = [ordered]@{}; identity_field = '' }))
        mcp = [ordered]@{ transport = 'stdio'; command = 'npx'; args = @('-y', '@supabase/mcp-server-supabase@0.13.0', '--read-only'); secret_env = @('SUPABASE_ACCESS_TOKEN') } }),
    # ------------------------------------------------ Efficiency and computer use (added 2026-09-24)
    (Mcp @{ id = 'repomix'; cost = ''; name = 'Repository Packer (Repomix)'; version = '1.18.1'; license = 'MIT'; author = 'Kazuki Yamada (Repomix)'; homepage = 'https://github.com/yamadashy/repomix'; date = '2026-09-21'
        desc = 'Packs a repository or folder into one compact, AI-friendly file with token counts. Its compress mode keeps only code structure (signatures, types) so agents read far fewer tokens.'; cat = @('Efficiency', 'Developer'); caps = @('mcp'); perms = @('read_files', 'run_commands', 'mcp'); tools = @('node'); tags = @('popular')
        pop = @{ label = 'GitHub stars of yamadashy/repomix'; value = 28482 }; notes = 'Pinned npm version 1.18.1 (published 2026-09-21). Runs on this computer; it has a built-in secret scanner (Secretlint).'
        note = 'The project states that compress mode reduces tokens by about 70%; AGEX has not measured this and the saving depends on the code.'
        mcp = [ordered]@{ transport = 'stdio'; command = 'npx'; args = @('-y', 'repomix@1.18.1', '--mcp'); secret_env = @() } }),
    (Mcp @{ id = 'serena'; cost = ''; trust = 'community'; name = 'Semantic Code Navigation (Serena)'; version = '1.7.0'; license = 'GPL-3.0-or-later (SolidLSP part MIT)'; author = 'Oraios AI'; homepage = 'https://github.com/oraios/serena'; date = '2026-08-09'
        desc = 'Finds symbols, references and definitions with language servers, so agents read the few functions they need instead of whole files.'; cat = @('Efficiency', 'Developer'); caps = @('mcp'); perms = @('read_files', 'write_files', 'run_commands', 'mcp'); tools = @('uv'); tags = @('popular', 'advanced')
        pop = @{ label = 'GitHub stars of oraios/serena'; value = 29773 }; notes = 'Pinned PyPI version 1.7.0 of serena-agent (published 2026-08-09). Runs on this computer and may download language servers on first use.'
        note = 'It includes editing tools; agents still follow AGEX approvals. Needs Python through uv.'; risk = 'Can edit files and run language servers.'
        mcp = [ordered]@{ transport = 'stdio'; command = 'uvx'; args = @('--from', 'serena-agent==1.7.0', 'serena', 'start-mcp-server'); secret_env = @() } }),
    (Mcp @{ id = 'figma-context'; cost = 'free-tier'; trust = 'community'; name = 'Figma Designs (Framelink)'; version = '0.13.2'; license = 'MIT'; author = 'GLips (Framelink)'; homepage = 'https://github.com/GLips/Figma-Context-MCP'; date = '2026-06-18'
        desc = 'Lets agents read the layout and styles of your Figma designs to build matching screens. Read only.'; cat = @('Design'); caps = @('mcp'); perms = @('network', 'run_commands', 'mcp'); tools = @('node'); tags = @('popular')
        pop = @{ label = 'GitHub stars of GLips/Figma-Context-MCP'; value = 15900 }; notes = 'Pinned npm version 0.13.2 (published 2026-06-18). Design data is read from Figma with your token.'
        auth = (Key 'FIGMA_API_KEY' 'Figma personal access token' 'https://help.figma.com/hc/en-us/articles/8085703771159-Manage-personal-access-tokens' ([ordered]@{ url = 'https://api.figma.com/v1/me'; header = 'X-Figma-Token'; scheme = ''; extra_headers = [ordered]@{}; identity_field = 'handle' }) 'A token with read-only file access is enough.')
        mcp = [ordered]@{ transport = 'stdio'; command = 'npx'; args = @('-y', 'figma-developer-mcp@0.13.2', '--stdio'); secret_env = @('FIGMA_API_KEY') } }),
    (Mcp @{ id = 'windows-mcp'; cost = ''; trust = 'community'; platforms = @('windows'); name = 'Windows Computer Use (Windows-MCP)'; version = '0.8.5'; license = 'MIT'; author = 'CursorTouch'; homepage = 'https://github.com/CursorTouch/Windows-MCP'; date = '2026-08-01'
        desc = 'Lets agents see the Windows desktop (screenshots and the UI tree) and click, type, scroll and open apps.'; cat = @('Automation'); caps = @('mcp'); perms = @('read_files', 'write_files', 'run_commands', 'network', 'mcp'); tools = @('uv'); tags = @('advanced')
        pop = @{ label = 'GitHub stars of CursorTouch/Windows-MCP'; value = 7153 }; notes = 'Pinned PyPI version 0.8.5 (published 2026-08-01). Needs Python 3.13 through uv.'
        note = 'Use it only with the Computer Operator team, and take control or stop from the workspace panel at any time.'
        risk = 'Full control of your desktop: it can click anything, type anywhere, run PowerShell and change files. The project warns it can perform irreversible operations. AGEX asks before every use.'
        mcp = [ordered]@{ transport = 'stdio'; command = 'uvx'; args = @('windows-mcp==0.8.5', 'serve'); secret_env = @() } })
)

$packs = @(
    [ordered]@{ id = 'starter'; name = 'Recommended starter pack'; description = 'A light start: GitHub, web research, a browser, debugging, code review and testing.'; skills = @('github-mcp', 'web-fetch', 'playwright-mcp', 'systematic-debugging', 'requesting-code-review', 'test-driven-development') },
    [ordered]@{ id = 'developer-essentials'; name = 'Developer Essentials'; description = 'Plan, build test-first, debug, verify and review.'; skills = @('writing-plans', 'executing-plans', 'test-driven-development', 'systematic-debugging', 'verification-before-completion', 'requesting-code-review', 'receiving-code-review', 'context7') },
    [ordered]@{ id = 'github-workflow'; name = 'GitHub Workflow'; description = 'Issues, pull requests, CI fixes and clean branches.'; skills = @('github-mcp', 'gh-fix-ci', 'gh-address-comments', 'finishing-a-development-branch', 'using-git-worktrees', 'caveman-commit') },
    [ordered]@{ id = 'web-app-builder'; name = 'Web App Builder'; description = 'Design, build and test web front ends in a real browser.'; skills = @('frontend-design', 'playwright-mcp', 'chrome-devtools-mcp', 'webapp-testing', 'context7') },
    [ordered]@{ id = 'research'; name = 'Research Pack'; description = 'Web search, page reading and official documentation.'; skills = @('web-fetch', 'exa-search', 'context7', 'deepwiki', 'microsoft-learn', 'aws-docs', 'cloudflare-docs') },
    [ordered]@{ id = 'local-private'; name = 'Local / Private Pack'; description = 'Instruction-only skills that need no network and no account.'; skills = @('test-driven-development', 'systematic-debugging', 'writing-plans', 'verification-before-completion', 'requesting-code-review', 'property-based-testing', 'caveman') },
    [ordered]@{ id = 'devops'; name = 'DevOps Pack'; description = 'CI, deployments and production errors.'; skills = @('gh-fix-ci', 'vercel-deploy', 'netlify-deploy', 'cloudflare-deploy', 'render-deploy', 'sentry-mcp', 'supabase-mcp') },
    [ordered]@{ id = 'security'; name = 'Security Pack'; description = 'Secure coding, threat models and static analysis.'; skills = @('security-best-practices', 'security-threat-model', 'differential-review', 'semgrep-scan', 'security-ownership-map', 'codeql-setup') },
    [ordered]@{ id = 'token-saver'; name = 'Token Saver Pack'; description = 'Read less, answer shorter: repository packing, symbol-level code navigation and short answers.'; skills = @('repomix', 'serena', 'caveman', 'caveman-review') },
    [ordered]@{ id = 'documents-data'; name = 'Documents & Data Pack'; description = 'PDFs, notebooks, Notion and web data extraction.'; skills = @('pdf-documents', 'jupyter-notebook', 'notion-mcp', 'firecrawl') }
)

$catalog = [ordered]@{ format = 'agex-skill-catalog'; format_version = 2; updated = $asOf; source = 'Curated by AGEX. Every instruction skill is pinned to a commit and verified by SHA-256; every MCP server is pinned to a package version or is an official hosted endpoint.'; skills = $skills; packs = $packs }
$json = $catalog | ConvertTo-Json -Depth 12
[IO.File]::WriteAllText($out, $json, [Text.UTF8Encoding]::new($false))
"wrote $($skills.Count) skills and $($packs.Count) packs"
