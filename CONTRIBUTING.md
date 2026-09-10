# Contributing

Contributions are welcome through pull requests.

## Before opening a pull request

- Work from a fork or feature branch; do not expect direct write access to this repository.
- Keep changes focused and explain the user-visible and security impact.
- Do not commit secrets, credentials, internal URLs, workstation evidence, user data, or production diagnostic bundles.
- Do not attach raw `Gradient-PC-Health-Check-E2E-*.zip` evidence to a public issue or pull request.
- Do not weaken the remediation allow-list, canonical-path checks, nonce/session binding, reparse-point protections, or CI security gates without an explicit security rationale.

## Required checks

The project targets Windows 11 x64 and .NET 8. Pull requests should pass:

- `Windows EXE` build and self-tests;
- NuGet vulnerability audit;
- published EXE security negative tests;
- `E2E Evidence Analyzer` synthetic tests;
- PowerShell parser checks.

GitHub Actions on external pull requests run with read-only repository permissions. Release artifacts are produced only from trusted repository runs and official releases are created only from successful pushes to `main`.

## Pull request content

Please include:

- what changed and why;
- how it was tested;
- whether remediation/elevation behavior changed;
- whether diagnostic collection or report contents changed;
- whether the change affects release packaging, GitHub Actions, or dependencies.

## Security reports

Do not report security vulnerabilities in a public issue. Follow [`SECURITY.md`](SECURITY.md).

## Licensing

By submitting a contribution for inclusion in this project, you agree that your contribution is licensed under the Apache License, Version 2.0, unless explicitly stated otherwise.

The project name, G-shield artwork, logos, and other branding are subject to the separate trademark/branding notice in [`NOTICE`](NOTICE).