# G PC Health Check

Windows 11 x64 Service Desk utility for workstation diagnostics, explainable health assessment, before/after reporting, and controlled remediation.

Current project version: **0.4.4**. The application is a self-contained single-file `G-PC-Health-Check.exe`.

> **Security / privacy:** never attach an unreviewed `G-PC-Health-Check-E2E-*.zip` to a public Issue or Pull Request. Evidence may include workstation names, usernames, domain information, hardware/OS details and diagnostic reports. See [`SECURITY.md`](SECURITY.md).

## What 0.4.4 adds

The next Service Desk step now comes from the primary finding itself. An action recommended for a secondary problem can no longer replace it: low-space Temp cleanup must not hide the guidance to investigate a failing physical disk and check backups. Blank primary guidance falls back to manual investigation before changes, not an unrelated action.

Clipboard observations use the same stable critical-first, penalty-second ordering as the triage card. The eight-observation limit is applied after prioritization; longer lists state the number omitted and point to the full HTML/JSON report. Presentation does not change scores, findings or selected actions.

The release adds 19 synthetic regression scenarios. Details and pilot checks: [`docs/releases/0.4.4.md`](docs/releases/0.4.4.md).

## Diagnostic capabilities

The application collects CPU, RAM, logical/physical disks, Windows/build, processes, Event Log, startup, security products, network and Windows Update signals. CPU and disk samples use short-series medians to reduce transient false positives. Disk-pressure findings require both elevated busy percentage and queue depth; repeated events are grouped by Provider/Event ID.

The dashboard displays score, diagnostic coverage, significant findings and the next Service Desk step. Missing signals appear in the System view, clipboard summary and reports. Process/event columns sort by typed numeric values. Scanning shows progress and elapsed time without a global busy cursor. HTML/JSON reports support before/after comparison.

Since 0.4.3, every missing weighted diagnostic signal produces a data warning even if coverage is still in the numeric HIGH band. Missing data does not subtract score points; confirmed CRIT findings retain priority. Physical-health coverage requires a known status for every discovered disk. An unknown disk status is missing data, not proof of failure. Unmeasured system-disk space is explicitly identified in the optional cleanup recommendation.

The score is not a probability of health. See [`docs/ASSESSMENT-MODEL.md`](docs/ASSESSMENT-MODEL.md), [`docs/KNOWN-LIMITATIONS.md`](docs/KNOWN-LIMITATIONS.md) and [`docs/E2E-TEST-PLAN.md`](docs/E2E-TEST-PLAN.md).

## Remediation security boundary

Normal-user diagnostics and reports do not need elevation. `CleanTemp` removes only old ordinary files under the interactive user's `%LOCALAPPDATA%\Temp`; `FlushDns` is also available without the administrative worker.

`CleanTemp` does not clean Windows Temp, Prefetch or other system directories. It does not traverse reparse points, junctions or symbolic links, and refuses to run when the GUI is elevated.

`DISM /Online /Cleanup-Image /RestoreHealth` and `SFC /scannow` require UAC. The administrative worker is allowed only from the exact canonical installation path:

`%ProgramFiles%\G\PCHealthCheck\G-PC-Health-Check.exe`

The worker validates that path, rejects reparse points, accepts only the fixed IDs `FlushDns`, `Dism`, `Sfc`, rejects `CleanTemp` and unknown/mixed action lists, and uses a one-time session ID and nonce over a local named pipe. Legacy bootstrap from user-writable locations remains disabled. See [`docs/SECURITY.md`](docs/SECURITY.md).

## Build, CI and releases

The `Windows EXE` workflow runs on main pushes, pull requests and manual dispatch, with read-only repository permissions and pinned Actions. Its gates include PowerShell parsing/smoke, deterministic branding provenance, NuGet audit including transitive dependencies, warnings-as-errors build, source and single-EXE self-tests, negative worker tests, exact FileVersion, SHA-256 and pilot-package UTF-8 validation.

The protected main branch requires a pull request and passing `build`, `analyzer` and `supply-chain-smoke` checks against the current base, with linear history and protection against deletion/non-fast-forward updates.

A successful main push build triggers release publication and supply-chain attestations. Both validate the triggering run and exact tested SHA, download artifacts from that run and re-verify the EXE checksum. Existing release tags are not overwritten.

The supply-chain workflow generates an SPDX 2.2 SBOM using the pinned Microsoft SBOM Tool, signs EXE/pilot build provenance with GitHub Artifact Attestations, binds the SBOM to the EXE, and retains SBOM/checksum artifacts. Linux restore is dependency-metadata-only with Windows targeting enabled; the Windows application is built on Windows. See [`docs/SUPPLY-CHAIN.md`](docs/SUPPLY-CHAIN.md).

```powershell
gh attestation verify G-PC-Health-Check.exe --repo bajoicheg/g-pc-health-check
```

Pull-request builds are not published releases and do not receive main-release attestations. A CI success is not a substitute for Windows 11 workstation GUI/UAC/remediation E2E.

## Building locally

Prerequisites: Windows 11 x64 and .NET 8 SDK.

```powershell
dotnet restore src/G.PcHealthCheck/G.PcHealthCheck.csproj
dotnet build src/G.PcHealthCheck/G.PcHealthCheck.csproj -c Release -warnaserror
dotnet run --project src/G.PcHealthCheck/G.PcHealthCheck.csproj -c Release -- --selftest
dotnet publish src/G.PcHealthCheck/G.PcHealthCheck.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish
```

## Contributing, licensing and signing

See [`CONTRIBUTING.md`](CONTRIBUTING.md). Use synthetic or redacted data for public reports; report vulnerabilities privately as described in [`SECURITY.md`](SECURITY.md).

Source code is licensed under Apache License 2.0; see [`LICENSE`](LICENSE). The G name, G-shield and related branding are not granted under that license and remain reserved to their owners; see [`NOTICE`](NOTICE).

The PE is not yet Authenticode-signed. Build/SBOM provenance does not replace a Windows publisher signature. For managed deployment, verify the checksum and attestation and use an approved distribution channel; signing and publisher controls remain recommended before broad rollout.
