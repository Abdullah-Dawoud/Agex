# Setup Dashboard

Generate dashboard:

```powershell
.\setup.ps1 dashboard
```

Open generated `reports/dashboard.html`. Use `-Open` to launch it:

```powershell
.\setup.ps1 dashboard -Open
```

Dashboard consumes `doctor.ps1 -Json`. It shows capability status, versions where safe, global/project boundaries, and known warnings. It never displays keys, tokens, auth files, or memory contents.

Dashboard groups retained capabilities into Developer and Worker sections. `AUTH REQUIRED` means connector capability is intentionally not installed or authenticated; it is not a broken health check.
