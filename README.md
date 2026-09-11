# G PC Health Check

Windows 11 x64 Service Desk utility for workstation diagnostics, explainable health assessment, before/after reporting, and controlled remediation.

Current project version: **0.5.1**. The application is a self-contained single-file `G-PC-Health-Check.exe`.

> **Security / privacy:** never attach unreviewed diagnostic reports to a public Issue or Pull Request. Evidence may include workstation names, usernames, domain information, hardware/OS details, device names and network addresses. See [`SECURITY.md`](SECURITY.md).

## What 0.5.1 fixes

Assessment, the dashboard, clipboard and HTML/before-after reports now use one system-volume selector. They resolve the local Windows volume instead of assuming C:, normalize equivalent drive-root forms, and never substitute another disk when the expected volume is absent. Invalid or ambiguous disk measurements reduce coverage without fabricating a confirmed fault or preselecting cleanup; a measured zero free space remains critical. The System tab and report identify the Windows volume explicitly.

The fix includes 24 synthetic regression scenarios. It applies to local workstation diagnostics, not offline import/reanalysis of another computer's report. Collector, worker/elevation boundaries, dependencies and workflow permissions are unchanged. Details and pilot checks: [`docs/releases/0.5.1.md`](docs/releases/0.5.1.md).

## What 0.5.0 adds

The menu **Типовые проблемы: сеть, печать, устройства** opens an additional diagnostic window in the same EXE:

- local network/IP/DNS configuration checks, including IPv6-only and multiple-NIC cases;
- installed printer/default/offline/driver-reported state and Print Spooler status;
- present Plug-and-Play device error codes, distinguishing disabled/disconnected devices from actionable faults;
- explicit `UNKNOWN` when a collector or device state is unavailable;
- evidence and guided resolution steps, fixed Windows Settings shortcuts, scan/cancel, clipboard summary and local HTML/JSON exports with previous/current snapshots;
- an optional, separately confirmed DNS-cache flush that reuses the existing allow-listed action, followed by a configuration rescan. It is not offered when no usable IP or configured DNS is available.

These additional results do not silently change the existing Health Score/Coverage model. IP/DNS configuration does not prove resource reachability; a printer driver's status does not prove successful printing. `INFO` is contextual evidence, not a healthy verdict. A successful command or a repeated scan does not prove that the user's symptom is resolved.

No automatic network/Winsock reset, DHCP release, DNS/proxy/VPN changes, print-job deletion, Spooler restart, driver installation or device enabling is added. Existing worker/elevation boundaries and dependencies are unchanged.

The practical 12-family backlog, technical sources and automation boundaries are documented in [`docs/COMMON-PROBLEMS.md`](docs/COMMON-PROBLEMS.md). This is a product-priority catalogue, not a measured incident-frequency ranking. Version notes and pilot checks: [`docs/releases/0.5.0.md`](docs/releases/0.5.0.md).

## Diagnostic capabilities

The main window collects CPU, RAM, logical/physical disks, Windows/build, processes, Event Log, startup, security products, network and Windows Update signals. CPU and disk samples use short-series medians to reduce transient false positives. Disk-pressure findings require both elevated busy percentage and queue depth; repeated events are grouped by Provider/Event ID.

The dashboard displays score, diagnostic coverage, significant findings and the next Service Desk step. Missing signals appear in the System view, clipboard summary and reports. Process/event columns sort by typed numeric values. Scanning shows progress and elapsed time without a global busy cursor. HTML/JSON reports support before/after comparison.

Since 0.4.3, every missing weighted diagnostic signal produces a data warning even if coverage remains in the numeric HIGH band. Missing data does not subtract score points; confirmed CRIT findings retain priority. Physical-health coverage requires known status for every discovered disk; unknown health is not proof of failure. Unmeasured system-disk space is explicitly identified.

Since 0.4.4, the next step comes from the primary finding itself, never an unrelated action for a secondary issue. Clipboard observations use the same critical-first/penalty-second ordering before truncation and state the omitted count. Presentation does not change scores or selected actions.

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

Pull-request builds are not published releases and do not receive main-release attestations. A CI success is not a substitute for managed Windows 11 workstation GUI/UAC/remediation E2E. WMI cancellation is cooperative; per-operation timeouts cannot guarantee that every third-party provider returns promptly.

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
