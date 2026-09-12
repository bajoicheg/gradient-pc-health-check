# Contributing

Contributions are welcome through focused pull requests. Read [AGENTS.md](AGENTS.md) and [the development workflow](docs/DEVELOPMENT.md) before work, especially when resuming after a connector failure.

## Fast local loop

Use a Windows checkout with Git, PowerShell 7 and the SDK pinned in `global.json`:

```powershell
pwsh -NoProfile -File tools/dev/Invoke-DevCheck.ps1 -Profile Quick
pwsh -NoProfile -File tools/dev/Invoke-DevCheck.ps1 -Profile Full
```

Quick compiles and runs all source tests without publishing an EXE. Full adds packaged-EXE and portable checks. Both save per-stage results and logs to ignored `artifacts/dev/`. Neither is a release or a substitute for required GitHub checks and real Windows 11 pilot acceptance. The scripts do not provision a development machine, authenticate to GitHub or upload source.

## Before opening a pull request

- Work from a fork or feature branch; no direct writes to main.
- Keep one coherent task per PR; record exact base/head/test SHAs and the next step in its checkpoint block.
- Do not commit credentials, secrets, internal infrastructure details, user data or raw diagnostic/E2E bundles.
- Preserve worker allow-list, nonce/session and relevant reparse-point checks; explain and test any privilege-boundary change.
- Preserve arbitrary portable EXE location/name and elevated read-only Temp preview. The old canonical Program Files-only restriction was removed in 0.5.2 and must not be restored accidentally.
- On a tool-evaluation denial, stop that operation and record a private incident/checkpoint. Do not evade it with another endpoint, encoding or account. Read remote state before repeating an uncertain write.

## Required checks and release boundaries

The Windows x64/.NET project retains its Windows EXE build/self-tests, transitive NuGet audit, packaged/portable tests, PowerShell gates, E2E Evidence Analyzer and Supply Chain Smoke checks. The path-filtered Developer Tools workflow additionally validates development helpers when they change; it is not a new blanket required check for unrelated PRs.

External PRs run with read-only repository permissions. Only the existing successful exact-main-push workflow publishes release assets. Do not replace assets of an existing version, label local/PR builds as releases, or bump product versions for development-process-only maintenance. Hosted Windows Server CI does not replace user-session/GUI/UAC tests on Windows 11.

## Security and licensing

Report vulnerabilities privately as described in [SECURITY.md](SECURITY.md). By submitting a contribution, you agree it is licensed under Apache License 2.0 unless explicitly stated otherwise. G branding remains subject to [NOTICE](NOTICE).
