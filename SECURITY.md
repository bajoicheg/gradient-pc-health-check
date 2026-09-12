# Security Policy

## Supported versions

Only the latest published release and the current `main` branch are supported for security fixes.

## Reporting a vulnerability

Please do **not** open a public issue containing exploit details, credentials, workstation evidence, internal hostnames, usernames, domain names, screenshots with sensitive data, or E2E evidence ZIP files.

Preferred reporting path:

1. Use GitHub **Private vulnerability reporting** / **Report a vulnerability** on the repository Security page when that option is available.
2. If private vulnerability reporting is not available, contact the repository maintainer privately through the GitHub profile before sending technical details or evidence.

A useful report should include the affected version/SHA, attack prerequisites and privilege level, reproducible steps with synthetic or redacted data, expected/actual boundary, impact and suggested mitigation. Do not include production credentials or unreviewed workstation exports.

## Security model

The application model is documented in [`docs/SECURITY.md`](docs/SECURITY.md).

- Diagnostics and user Temp cleanup are non-privileged; `CleanTemp` is never accepted by the elevated worker.
- Administrative actions require explicit selection/confirmation and an administrative token. Standard-user launches use Windows UAC.
- Since 0.5.2, administrative actions are portable: the EXE may have any name/location. The former canonical Program Files gate was intentionally removed at the owner's request, not replaced with a signing check.
- Worker commands remain fixed; session, pipe, nonce, unknown/mixed action lists and results are validated.
- Legacy copy/install bootstrap remains disabled because the current EXE is launched directly.

Portable elevation does not claim that a writable executable location is protected against replacement or side-loading. Windows execution policy and access controls still apply. The PE is not Authenticode-signed; release checksums and build/SBOM provenance are separate controls.

## Diagnostic evidence is sensitive

`tools/e2e/Collect-E2EEvidence.ps1` collects support information such as computer/user/domain names, OS/hardware data, executable hashes/versions and recent reports. Pass `-SourceExe` with the actual filename when using a renamed executable.

Treat generated evidence as internal support data. **Never attach an unreviewed evidence ZIP to a public GitHub issue or pull request.**
