# Project Initialization Design

Command: `agex init-project <path>`.

## Goal

Prepare a new repository with small, reviewable project-local context. Do not turn initialization into a full project generator.

## Implemented behavior

1. Verify path exists. Warn when path is not a Git repository.
2. Inspect for existing `AGENTS.md`, `docs/`, `.gitignore`, and project manifests.
3. Never overwrite existing files. Offer `.example` or `.template` files when a name exists.
4. Add missing `AGENTS.md` from `templates/AGENTS.project.md`.
5. With `-CreateDocs`, add missing architecture/security/decisions placeholders.
6. Support `-DryRun`; never overwrite existing files.

## Scope boundaries

Project initialization does not install tools, edit Codex global config, create memory services, modify credentials, create databases, or infer schemas. It does not copy setup repository knowledge into project memory.

## Removed code-intelligence dependency

Initialization does not install or configure Serena. Use Codex, `rg`, Git, and project tests for code inspection.

## Stop condition

Stop after templates and optional project config are prepared. Script does not install tools, edit Codex config, create memory namespaces, modify credentials, or change permissions.
