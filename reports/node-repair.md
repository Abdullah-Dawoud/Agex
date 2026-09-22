# Node Repair Report

Date: 2026-09-19

## Detected manager

- Manager: NVM for Windows Community Edition.
- Version: `2.0.1-hotfix.1`.
- Root: `C:\Users\isc\AppData\Local\Author Software\nvm`.
- Mode: `shim`.
- Installed version: `v22.23.2`.
- Install root: `C:\Users\isc\AppData\Local\Author Software\nvm\installs`.
- Shim directory: `C:\Users\isc\AppData\Local\Author Software\nvm\.shim`.
- Current default: `vnone`.

Official NVM for Windows documents `nvm use <version>` as version selection and supports `shim`/`link` modes. See [official operating modes](https://docs.nvm-windows.com/features/modes/) and [official project](https://github.com/nvm-windows/nvm).

## Evidence

Direct installed runtime works:

```text
node v22.23.2
npm 10.9.8
npx 10.9.8
```

Normal command resolution fails because `.nodejs` points at an inactive shim state:

```text
No active Node.js version is configured. Run `nvm install <version>` then `nvm use <version>`.
```

`nvm use 22.23.2` was attempted. It failed before switching:

```text
registry: write denied for all key paths: Access is denied.
```

`nvm env` confirms one installed version, healthy runtime ACLs, shim mode, and default not set. Existing Node processes use the installed absolute path or Codex's bundled `cua_node`; they do not prove fresh-shell activation works.

## Host verification update

User verified from a fresh, normal, non-elevated host PowerShell:

```text
nvm default  -> v22.23.2
node         -> v22.23.2
npm          -> 10.9.8
npx          -> 10.9.8
```

The Codex sandbox still cannot see the host registry state. Treat that as sandbox visibility, not host Node failure.

## Repair status

No elevation, registry edit, PATH rewrite, version deletion, or new Node distribution was used.

Host repair is complete. Do not use registry hacks or ACL weakening.

## Cavemem gate

Host Node foundation passed. Cavemem testing used existing Node v22.23.2.
