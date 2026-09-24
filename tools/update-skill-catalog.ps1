# Regenerates src/Agex.Core/Skills/skills-catalog.json (maintainers only).
#
#   powershell -File tools/update-skill-catalog.ps1 [-AsOf 2026-09-24]
#
# Clones the source repositories of the curated instruction skills (line endings
# untouched), pins each skill to the current commit and records the SHA-256 of
# every file. Review the catalog diff before committing: every change to a
# pinned commit changes what users install. MCP package versions and popularity
# numbers are edited by hand below (from npm/PyPI/GitHub on the -AsOf date).
param([string]$AsOf = (Get-Date -Format 'yyyy-MM-dd'))
$ErrorActionPreference = 'Stop'
$research = Join-Path ([IO.Path]::GetTempPath()) ('agex-skill-sources-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $research | Out-Null
foreach ($repo in 'obra/superpowers', 'anthropics/skills', 'openai/skills') {
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
    $files = @(Get-Files $clone $s.base $s.exclude)
    $extra = @()
    if ($s.license_file) {
        $lf = Join-Path $research "$clone\$($s.license_file)"
        $extra += [ordered]@{ from = $s.license_file; to = 'LICENSE'; sha256 = (Get-FileHash $lf -Algorithm SHA256).Hash.ToLowerInvariant(); size = (Get-Item $lf).Length }
    }
    [ordered]@{
        id = $s.id; name = $s.name; kind = 'instructions'; description = $s.desc; author = $s.author; version = $commit.Substring(0, 7)
        license = $s.license; homepage = "https://github.com/$($s.repo)/tree/$commit/$($s.base)"; categories = $s.cat; recommended = [bool]$s.rec
        trust = 'curated'; capabilities = $s.caps; permissions = $s.perms; supported_agents = $(if ($s.agents) { $s.agents } else { $cli })
        supported_platforms = $all; required_tools = @($s.tools | Where-Object { $_ }); min_agex_version = '2.0.0'; compatibility_note = [string]$s.note
        popularity = [ordered]@{ label = "GitHub stars of $($s.repo) (whole repository)"; value = $s.stars; as_of = $asOf }
        release_notes = "Pinned to commit $($commit.Substring(0, 7)) of $($s.repo)."
        source = [ordered]@{ repository = $s.repo; commit = $commit; base_path = $s.base; files = $files; extra_files = $extra }
    }
}

function Mcp([hashtable]$s) {
    [ordered]@{
        id = $s.id; name = $s.name; kind = 'mcp'; description = $s.desc; author = $s.author; version = $s.version
        license = $s.license; homepage = $s.homepage; categories = $s.cat; recommended = [bool]$s.rec
        trust = 'verified'; capabilities = $s.caps; permissions = $s.perms; supported_agents = @('codex', 'claude-code')
        supported_platforms = $all; required_tools = @($s.tools | Where-Object { $_ }); min_agex_version = '2.0.0'; compatibility_note = 'MCP servers are passed to Codex and Claude Code for each request. Antigravity and Gemini CLI read MCP servers only from their own settings, so AGEX does not pass it to them.' + $(if ($s.note) { ' ' + $s.note } else { '' })
        popularity = $(if ($s.pop) { [ordered]@{ label = $s.pop.label; value = $s.pop.value; as_of = $asOf } } else { $null })
        release_notes = $s.notes
        mcp = $s.mcp
    }
}

$skills = @(
    (Instr @{ id = 'systematic-debugging'; name = 'Systematic Debugging'; repo = 'obra/superpowers'; base = 'skills/systematic-debugging'; exclude = @('test-*.md', 'CREATION-LOG.md'); license = 'MIT'; license_file = 'LICENSE'; author = 'Jesse Vincent (Superpowers)'
        desc = 'Find the root cause before fixing: reproduce, trace, form one hypothesis at a time, and prove the fix.'; cat = @('Developer', 'Debugging'); rec = $false; caps = @('debugging'); perms = @('read_files', 'run_commands'); stars = 290751 }),
    (Instr @{ id = 'test-driven-development'; name = 'Test-Driven Development'; repo = 'obra/superpowers'; base = 'skills/test-driven-development'; license = 'MIT'; license_file = 'LICENSE'; author = 'Jesse Vincent (Superpowers)'
        desc = 'Write a failing test first, make it pass, then clean up. Keeps changes small and proven.'; cat = @('Developer', 'Testing'); rec = $true; caps = @('testing'); perms = @('read_files', 'write_files', 'run_commands'); stars = 290751 }),
    (Instr @{ id = 'verification-before-completion'; name = 'Verify Before Done'; repo = 'obra/superpowers'; base = 'skills/verification-before-completion'; license = 'MIT'; license_file = 'LICENSE'; author = 'Jesse Vincent (Superpowers)'
        desc = 'Agents must run the checks and show the evidence before they say work is finished.'; cat = @('Developer', 'Productivity'); rec = $false; caps = @('testing', 'code_review'); perms = @('read_files', 'run_commands'); stars = 290751 }),
    (Instr @{ id = 'writing-plans'; name = 'Writing Plans'; repo = 'obra/superpowers'; base = 'skills/writing-plans'; license = 'MIT'; license_file = 'LICENSE'; author = 'Jesse Vincent (Superpowers)'
        desc = 'Turn a request into a step-by-step implementation plan with files, tests and checkpoints.'; cat = @('Developer', 'Productivity'); rec = $false; caps = @('planning'); perms = @('read_files', 'write_files'); stars = 290751 }),
    (Instr @{ id = 'requesting-code-review'; name = 'Code Review'; repo = 'obra/superpowers'; base = 'skills/requesting-code-review'; license = 'MIT'; license_file = 'LICENSE'; author = 'Jesse Vincent (Superpowers)'
        desc = 'A structured review of finished work against its requirements, with issues ranked by severity.'; cat = @('Developer', 'Recommended'); rec = $true; caps = @('code_review'); perms = @('read_files', 'run_commands'); stars = 290751
        note = 'Written for agents that can start a separate reviewer; other agents follow the same checklist themselves.' }),
    (Instr @{ id = 'security-best-practices'; name = 'Security Best Practices'; repo = 'openai/skills'; base = 'skills/.curated/security-best-practices'; exclude = @('agents/*'); license = 'Apache-2.0'; author = 'OpenAI'
        desc = 'Language- and framework-specific security review (Go, JavaScript, Python web stacks) with concrete fixes.'; cat = @('Developer', 'Security'); rec = $false; caps = @('code_review'); perms = @('read_files'); stars = 27593 }),
    (Instr @{ id = 'security-threat-model'; name = 'Threat Model'; repo = 'openai/skills'; base = 'skills/.curated/security-threat-model'; exclude = @('agents/*'); license = 'Apache-2.0'; author = 'OpenAI'
        desc = 'Writes a threat model for the project: trust boundaries, assets, attack paths and mitigations.'; cat = @('Developer', 'Security'); rec = $false; caps = @('code_review', 'documents'); perms = @('read_files', 'write_files'); stars = 27593 }),
    (Instr @{ id = 'gh-fix-ci'; name = 'Fix GitHub CI'; repo = 'openai/skills'; base = 'skills/.curated/gh-fix-ci'; exclude = @('agents/*', 'assets/*'); license = 'Apache-2.0'; author = 'OpenAI'
        desc = 'Reads failing GitHub Actions checks on a pull request, explains the failure and proposes a fix.'; cat = @('Developer', 'DevOps', 'Git & GitHub'); rec = $false; caps = @('debugging'); perms = @('read_files', 'write_files', 'run_commands', 'network', 'github'); tools = @('gh', 'python'); stars = 27593 }),
    (Instr @{ id = 'gh-address-comments'; name = 'Address PR Comments'; repo = 'openai/skills'; base = 'skills/.curated/gh-address-comments'; exclude = @('agents/*', 'assets/*'); license = 'Apache-2.0'; author = 'OpenAI'
        desc = 'Collects review comments on the current pull request and works through them.'; cat = @('Developer', 'Git & GitHub'); rec = $false; caps = @('code_review'); perms = @('read_files', 'write_files', 'run_commands', 'network', 'github'); tools = @('gh', 'python'); stars = 27593 }),
    (Instr @{ id = 'playwright-cli'; name = 'Browser Automation (Playwright CLI)'; repo = 'openai/skills'; base = 'skills/.curated/playwright'; exclude = @('agents/*', 'assets/*'); license = 'Apache-2.0'; author = 'OpenAI'
        desc = 'Drive a real browser from the terminal: open pages, fill forms, take screenshots, extract data.'; cat = @('Web', 'Testing'); rec = $false; caps = @('browser', 'testing'); perms = @('run_commands', 'network', 'browser'); tools = @('node'); stars = 27593 }),
    (Instr @{ id = 'webapp-testing'; name = 'Web App Testing'; repo = 'anthropics/skills'; base = 'skills/webapp-testing'; license = 'Apache-2.0'; author = 'Anthropic'
        desc = 'Test a local web app with Playwright for Python: check behaviour, capture screenshots and console logs.'; cat = @('Developer', 'Testing', 'Web'); rec = $false; caps = @('testing', 'browser'); perms = @('read_files', 'run_commands', 'browser'); tools = @('python'); stars = 177839 }),
    (Instr @{ id = 'frontend-design'; name = 'Frontend Design'; repo = 'anthropics/skills'; base = 'skills/frontend-design'; license = 'Apache-2.0'; author = 'Anthropic'
        desc = 'Guidance for deliberate visual design of user interfaces: layout, typography and a distinct look.'; cat = @('Design', 'Web'); rec = $false; caps = @('documents'); perms = @('read_files', 'write_files'); stars = 177839 }),
    (Instr @{ id = 'pdf-documents'; name = 'PDF Documents'; repo = 'openai/skills'; base = 'skills/.curated/pdf'; exclude = @('agents/*', 'assets/*'); license = 'Apache-2.0'; author = 'OpenAI'
        desc = 'Read, create and check PDF files, including visual checks of rendered pages.'; cat = @('Documents'); rec = $false; caps = @('documents'); perms = @('read_files', 'write_files', 'run_commands'); tools = @('python'); stars = 27593 }),
    (Instr @{ id = 'jupyter-notebook'; name = 'Jupyter Notebooks'; repo = 'openai/skills'; base = 'skills/.curated/jupyter-notebook'; exclude = @('agents/*', 'assets/*.png', 'assets/*.svg'); license = 'Apache-2.0'; author = 'OpenAI'
        desc = 'Create clean, well-structured notebooks for experiments and tutorials.'; cat = @('Data'); rec = $false; caps = @('documents'); perms = @('read_files', 'write_files', 'run_commands'); tools = @('python'); stars = 27593 }),
    (Mcp @{ id = 'playwright-mcp'; name = 'Browser (Playwright MCP)'; version = '0.0.82'; license = 'Apache-2.0'; author = 'Microsoft'; homepage = 'https://github.com/microsoft/playwright-mcp'
        desc = 'Lets agents open web pages, click, type and read pages in a real browser.'; cat = @('Recommended', 'Web', 'Testing'); rec = $true; caps = @('browser'); perms = @('network', 'browser', 'run_commands', 'mcp'); tools = @('node')
        pop = @{ label = 'npm downloads of @playwright/mcp, last month'; value = 23800747 }; notes = 'Pinned npm version 0.0.82 (published 2026-09-18).'
        mcp = [ordered]@{ transport = 'stdio'; command = 'npx'; args = @('-y', '@playwright/mcp@0.0.82'); secret_env = @() } }),
    (Mcp @{ id = 'chrome-devtools-mcp'; name = 'Chrome DevTools'; version = '1.10.1'; license = 'Apache-2.0'; author = 'Google Chrome DevTools team'; homepage = 'https://github.com/ChromeDevTools/chrome-devtools-mcp'
        desc = 'Lets agents inspect a Chrome page: console, network requests and performance traces.'; cat = @('Web', 'Debugging'); rec = $false; caps = @('browser', 'debugging'); perms = @('network', 'browser', 'run_commands', 'mcp'); tools = @('node')
        pop = @{ label = 'npm downloads of chrome-devtools-mcp, last month'; value = 7881886 }; notes = 'Pinned npm version 1.10.1 (published 2026-09-23).'; note = 'Needs Google Chrome.'
        mcp = [ordered]@{ transport = 'stdio'; command = 'npx'; args = @('-y', 'chrome-devtools-mcp@1.10.1'); secret_env = @() } }),
    (Mcp @{ id = 'context7'; name = 'Library Docs (Context7)'; version = '4.1.1'; license = 'MIT'; author = 'Upstash'; homepage = 'https://github.com/upstash/context7'
        desc = 'Gives agents current documentation for programming libraries instead of outdated memory.'; cat = @('Recommended', 'Research', 'Developer'); rec = $true; caps = @('web_research'); perms = @('network', 'run_commands', 'mcp'); tools = @('node')
        pop = @{ label = 'npm downloads of @upstash/context7-mcp, last month'; value = 3451044 }; notes = 'Pinned npm version 4.1.1 (published 2026-09-14). Library names in your requests are sent to the Context7 service.'
        mcp = [ordered]@{ transport = 'stdio'; command = 'npx'; args = @('-y', '@upstash/context7-mcp@4.1.1'); secret_env = @() } }),
    (Mcp @{ id = 'web-fetch'; name = 'Web Research (Fetch)'; version = '2026.8.18'; license = 'MIT'; author = 'Model Context Protocol project'; homepage = 'https://github.com/modelcontextprotocol/servers/tree/main/src/fetch'
        desc = 'Lets agents download a web page and read it as text.'; cat = @('Recommended', 'Research', 'Web'); rec = $true; caps = @('web_research'); perms = @('network', 'run_commands', 'mcp'); tools = @('uv')
        pop = $null; notes = 'Pinned PyPI version 2026.8.18.'; note = 'It can reach local network addresses; use it only with sites you trust.'
        mcp = [ordered]@{ transport = 'stdio'; command = 'uvx'; args = @('mcp-server-fetch==2026.8.18'); secret_env = @() } }),
    (Mcp @{ id = 'github-mcp'; name = 'Git & GitHub'; version = 'remote'; license = 'MIT'; author = 'GitHub'; homepage = 'https://github.com/github/github-mcp-server'
        desc = 'Lets agents read and manage GitHub issues, pull requests, reviews and Actions with your token.'; cat = @('Recommended', 'Git & GitHub', 'DevOps'); rec = $true; caps = @('mcp'); perms = @('network', 'github', 'mcp'); tools = @()
        pop = $null; notes = 'Uses GitHub''s hosted MCP endpoint. Your token is stored in the system keychain and passed by environment variable, never on a command line.'; note = 'Needs a GitHub personal access token.'
        mcp = [ordered]@{ transport = 'http'; url = 'https://api.githubcopilot.com/mcp/'; bearer_secret = 'GITHUB_PERSONAL_ACCESS_TOKEN'; secret_env = @('GITHUB_PERSONAL_ACCESS_TOKEN') } })
)

$catalog = [ordered]@{ format = 'agex-skill-catalog'; format_version = 1; updated = $asOf; source = 'Curated by AGEX. Every instruction skill is pinned to a commit and verified by SHA-256; every MCP server is pinned to a package version.'; skills = $skills }
$json = $catalog | ConvertTo-Json -Depth 12
[IO.File]::WriteAllText($out, $json, [Text.UTF8Encoding]::new($false))
"wrote $($skills.Count) skills"
