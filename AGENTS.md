# Setup Repository Instructions

## Core rules

- Inspect current files, configuration, and tool state before modifying anything.
- Reuse existing tools and configuration before adding dependencies.
- Treat current source code and tests as truth. Persistent memory is supplementary.
- Keep changes small, focused, reversible, and easy to validate.
- Preserve existing Codex, Git, SSH, repository, MCP, PATH, and credential configuration unless change is explicitly approved.
- Never expose or commit secrets, tokens, credentials, cookies, auth files, or connection strings.
- Use the smallest relevant context and avoid always-on MCP servers.
- Run focused validation after every change.
- Document important architecture and security decisions.

## High-risk work

Authentication, authorization/RLS, migrations, payments, financial logic, secrets, production configuration, infrastructure, and destructive operations require stronger source/schema inspection and validation. Do not rely on persistent memory for these tasks.

## Windows rules

- Prefer PowerShell-compatible scripts.
- Do not install duplicate Node, Python, browser, container, or MCP stacks.
- Do not weaken ACLs or security policy to make a tool pass a check.
- Keep machine-specific paths and credentials outside tracked configuration.

## Change gate

Before finishing, run the narrowest relevant test or doctor check, inspect `git diff --check`, and report repository changes separately from system changes.
