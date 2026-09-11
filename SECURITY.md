# Security Policy

## Supported versions

Only the latest published release and the current `main` branch are supported for security fixes.

## Reporting a vulnerability

Please do **not** open a public issue containing exploit details, credentials, workstation evidence, internal hostnames, usernames, domain names, screenshots with sensitive data, or E2E evidence ZIP files.

Preferred reporting path:

1. Use GitHub **Private vulnerability reporting** / **Report a vulnerability** on the repository Security page when that option is available.
2. If private vulnerability reporting is not available, contact the repository maintainer privately through the GitHub profile before sending technical details or evidence.

A useful report should include:

- affected version and commit SHA, if known;
- attack prerequisites and required privilege level;
- reproducible steps or a minimal proof of concept;
- expected versus actual security boundary;
- impact;
- suggested mitigation, if available.

Do not include real corporate credentials or production workstation evidence. Synthetic or redacted reproductions are preferred.

## Security model

The detailed application security model is documented in [`docs/SECURITY.md`](docs/SECURITY.md).

Key boundaries include:

- diagnostics and user Temp cleanup are non-privileged;
- `CleanTemp` is not accepted by the elevated worker;
- administrative remediation is restricted to a fixed allow-list and a canonical installed executable path;
- privileged worker requests are bound to a one-time session/nonce over a local named pipe;
- unknown or mixed unknown remediation actions fail closed;
- legacy bootstrap from user-writable locations is disabled.

## Diagnostic evidence is sensitive

`tools/e2e/Collect-E2EEvidence.ps1` intentionally collects support information such as computer name, current user, domain/user information returned by Windows, OS/hardware data, executable hashes/versions, and recent PC Health Check reports.

Treat generated E2E evidence bundles as internal support data. **Never attach an unreviewed evidence ZIP to a public GitHub issue or pull request.**