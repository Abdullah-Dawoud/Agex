# Security notes

- Keep plugin IPC local-only. Prefer authenticated named pipes.
- Generate a per-user random secret. Never log it.
- Validate protocol version, request size, operation name, target identity, paths, units, and risk permissions before dispatch.
- Keep tool handlers explicit. Do not add `execute_command`, arbitrary PowerShell, arbitrary C#, or reflection-based Autodesk calls.
- Mark reads, edits, file writes, and destructive operations separately. Safe mode permits reads only.
- Back up and validate AI-client configuration before merge. Restore backup on validation failure.
- Do not log full model payloads or secrets.

The source contains API-thread, transaction, authentication, and permission safeguards. End-to-end plugin session validation remains required before production use.
