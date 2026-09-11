# Supply-chain verification

Gradient PC Health Check 0.3.7 provides cryptographically verifiable provenance for public builds without widening the application's runtime privilege boundary.

Version 0.3.6 introduced this pipeline, but its first attestation run stopped before SBOM generation with `NETSDK1100` when the Linux runner restored the Windows-targeting project. Version 0.3.7 fixes that metadata-only restore with `EnableWindowsTargeting=true` and supersedes 0.3.6 for the complete attested release path.

## What is produced

For each successful `Windows EXE` push build on `main`, the `Supply Chain Attestations` workflow consumes only artifacts from that exact successful run and produces:

- GitHub/Sigstore build-provenance attestation for `Gradient-PC-Health-Check.exe`;
- GitHub/Sigstore build-provenance attestation for the pilot ZIP;
- SPDX 2.2 SBOM generated with `Microsoft.Sbom.DotNetTool` 4.1.5;
- signed SBOM attestation binding that SPDX document to the EXE;
- SBOM JSON plus a SHA-256 checksum as an Actions artifact.

The workflow has `contents: read`, `actions: read`, `id-token: write` and `attestations: write`. It deliberately does not have `contents: write`.

## Trust boundary

The attestation job runs only when all of the following are true:

1. the triggering workflow is exactly `Windows EXE`;
2. that workflow completed successfully;
3. the source event was `push`;
4. the source branch was `main`;
5. the triggering run reports a syntactically valid 40-character Git SHA.

The job then checks out that exact SHA, downloads artifacts from that exact run ID, and independently verifies the EXE SHA-256 against the checksum produced by the Windows build before generating any attestation.

The Linux attestation runner performs only a dependency-metadata restore with `-p:EnableWindowsTargeting=true` so the SBOM component detector can inspect the Windows-targeting .NET project. It does not build or publish the Windows application. The release binary remains the one built and security-tested by the `Windows EXE` workflow on the Windows runner.

This prevents artifacts from an untrusted fork Pull Request from being promoted into signed public provenance.

## Verify a downloaded EXE

Install or update GitHub CLI, then run from the directory containing the downloaded EXE:

```powershell
gh attestation verify .\Gradient-PC-Health-Check.exe --repo bajoicheg/gradient-pc-health-check
```

Verification should identify `bajoicheg/gradient-pc-health-check` as the source repository and validate the artifact digest against a GitHub Artifact Attestation signed through Sigstore.

The traditional checksum can also be verified independently:

```powershell
(Get-FileHash .\Gradient-PC-Health-Check.exe -Algorithm SHA256).Hash.ToLowerInvariant()
Get-Content .\Gradient-PC-Health-Check.exe.sha256
```

Both values must match.

## SBOM

The SBOM is generated in SPDX 2.2 JSON format from:

- the final build drop containing the released EXE/checksum;
- the exact source tree at the tested commit, including the .NET project/component metadata.

The generated document is sanity-checked for SPDX version, package inventory and file inventory before attestation.

## What this does not provide

Artifact attestations do **not** Authenticode-sign the Windows executable. They protect provenance and integrity for consumers that verify the GitHub/Sigstore attestation, but Windows Explorer, SmartScreen, AppLocker publisher rules and WDAC publisher policies still require an Authenticode signing certificate.

Before broad enterprise deployment, the recommended next step is an organization-controlled code-signing process (preferably HSM/key-vault backed) and enforcement of the approved publisher/certificate chain through the endpoint control plane.
