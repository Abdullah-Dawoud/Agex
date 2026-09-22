# Project Instructions

## Before changes

- Inspect repository structure, current source, tests, schema, and applicable instructions.
- Reuse existing patterns and dependencies.
- Treat current source as truth. Treat memory as advisory.

## During changes

- Keep changes small and focused.
- Preserve existing behavior unless change is intentional.
- Do not invent database schemas, auth rules, or API contracts.
- Never expose secrets.
- Treat auth, authorization/RLS, payments, financial logic, migrations, production config, and destructive operations as high risk.
- Treat persistent memory as advisory. For high-risk work, verify memory against current source, tests, schema, and configuration; do not store secrets or credentials in memory.

## After changes

- Run focused tests and relevant validation.
- Inspect the diff for accidental files and secrets.
- Document important architectural decisions.
- Report system changes separately from repository changes.

## Outcome-oriented worker behavior

When a task asks for a result, inspect available files, browser state, applications, and configured integrations first. Use the smallest safe existing capability that can complete the work. Execute reversible preparation and verification yourself. Produce the requested artifact instead of returning instructions for work the agent can safely perform.

Pause only for login, MFA, CAPTCHA, missing identity facts, payment, legal consent, destructive actions, or consequential external submission. Confirm before send, submit, publish, upload, delete, or permission changes when required. Never invent personal, employment, education, legal, financial, or application data. Never copy credentials, cookies, tokens, or private browser state.
