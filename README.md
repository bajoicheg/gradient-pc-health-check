# G PC Health Check

Windows 11 x64 Service Desk utility for workstation diagnostics, explainable health assessment, before/after reporting, and controlled remediation.

Current project version: **0.6.0**. The application is a self-contained single-file `G-PC-Health-Check.exe`.

> **Security / privacy:** never publish unreviewed diagnostic reports. Evidence may contain workstation/user/domain names, file paths, startup commands and network addresses. See [`SECURITY.md`](SECURITY.md).

## New in 0.6.0 — inspect before acting

Open **Анализ → Предпросмотр очистки Temp…** for a metadata-only preview of the existing old-user-Temp cleanup scope: candidate count, logical size estimate, exact cutoff, 200 largest candidates, skipped links and access errors. Enumeration is bounded at 100,000 entries/30 seconds between provider calls; partial results are explicitly identified. Nothing is deleted. Actual cleanup remains a separately confirmed main-window action that rechecks current file eligibility.

Open **Анализ → Разбор автозагрузки…** for searchable read-only Run/RunOnce and Startup-folder evidence: names, raw commands/file references, account/scope and per-source collection status. Commands are not expanded or executed, shortcut targets are not resolved, and no entries are disabled. Presence is not proof of enabled state, performance impact or maliciousness. This is a defined subset, not full Autoruns coverage.

Both windows offer repeat/cancel, full row details, clipboard summary and local HTML/JSON exports. Cancelled or failed scans do not silently replace earlier snapshots. Exports retain the whole saved snapshot regardless of the current search filter. See [`docs/releases/0.6.0.md`](docs/releases/0.6.0.md) for supported sources, limits, test history and Windows 11 pilot checks. The ideas are independently implemented; third-party utilities and code are not bundled.

## Portable administrative actions (since 0.5.2)

**DISM/SFC can be launched from any EXE location and under any EXE filename**, including Downloads and renamed copies. No installation or fixed Program Files path is required. Start normally, choose actions and confirm them; administrative actions request UAC for the same executable. Move/rename the file only while the application is closed. Windows access and enterprise execution policies still apply; network paths must also be accessible to the administrative identity.

The worker validates its fixed action IDs, session, pipe name, nonce and administrative token. User Temp cleanup remains outside the elevated worker. The obsolete copy/install bootstrap is not used. Portability removes the old application-level deployment-path restriction; it does not eliminate writable-directory risks or add Authenticode signing.

## Diagnostic capabilities

The main window collects CPU, RAM, logical/physical disks, Windows/build, processes, Event Log, startup, security-product, network and Windows Update signals. CPU/disk values use short-series medians; disk-pressure findings require elevated busy percentage and queue depth together. Event findings consider repeated Provider/Event ID groups rather than raw counts alone.

The dashboard exposes score, coverage, findings and the next Service Desk step. Missing telemetry is not treated as healthy or fabricated as a confirmed fault. Primary guidance and clipboard observations prioritize important findings. System-volume selection is shared across assessment, dashboard and reports and does not assume C:.

The menu **Типовые проблемы: сеть, печать, устройства** adds local IP/DNS configuration, printer/Spooler and Plug-and-Play checks with evidence and guided next steps, repeat/cancel, snapshots and export. Fixed Windows Settings shortcuts assist manual investigation. Optional DNS-cache cleanup requires separate confirmation.

Configuration is not proof of resource reachability, printer-driver status is not proof of successful printing, and command success is not proof the symptom is resolved. Neither common-problem checks nor the 0.6.0 read-only reviews silently change the original health model.

See [`docs/ASSESSMENT-MODEL.md`](docs/ASSESSMENT-MODEL.md), [`docs/COMMON-PROBLEMS.md`](docs/COMMON-PROBLEMS.md), [`docs/KNOWN-LIMITATIONS.md`](docs/KNOWN-LIMITATIONS.md), [`CHANGELOG.md`](CHANGELOG.md) and [0.6.0 version notes](docs/releases/0.6.0.md). The common-problem catalogue is a product-priority backlog, not incident-frequency statistics.

## Remediation boundaries

Diagnostics and reports do not require elevation. `CleanTemp` removes only old ordinary files from the interactive user's `%LOCALAPPDATA%\Temp`; it does not clean Windows Temp/Prefetch or traverse encountered reparse points. It refuses to run in an already elevated GUI. `FlushDns` normally runs without the administrative worker.

DISM RestoreHealth and SFC /scannow require administrative rights. A standard-user GUI requests UAC for the current EXE irrespective of name/location. Combined batches keep `CleanTemp` in the parent user context. The worker accepts only `FlushDns`, `Dism`, `Sfc`, rejects `CleanTemp` and unknown/mixed requests, and returns session/nonce-bound results via a local named pipe. Cancelled UAC does not execute the batch.

No automatic network reset, DHCP release, DNS/proxy/VPN/GPO/EDR change, Spooler restart, print-job deletion, driver installation, startup disabling or automatic reboot is added. Details: [`docs/SECURITY.md`](docs/SECURITY.md).

## Build, CI and provenance

The `Windows EXE` workflow runs on main pushes, pull requests and manual dispatch with read-only repository permissions and pinned Actions. Gates cover PowerShell parsing, deterministic branding, transitive NuGet audit, warnings-as-errors build, source/single-EXE self-tests, portable worker checks, FileVersion, SHA-256 and pilot metadata/UTF-8.

Changes are integrated through PRs with required checks. Successful main builds trigger release publication and supply-chain attestations; both validate the exact tested SHA/run, download only that run's artifacts and recheck the EXE hash. Existing releases are not overwritten. SPDX SBOM and EXE/pilot provenance are described in [`docs/SUPPLY-CHAIN.md`](docs/SUPPLY-CHAIN.md).

```powershell
gh attestation verify G-PC-Health-Check.exe --repo bajoicheg/g-pc-health-check
```

PR builds are not published releases. Hosted Windows Server tests do not replace managed Windows 11 GUI/DPI/UAC, real hardware/provider and remediation checks. Provider cancellation is cooperative, not a hard timeout. See [`docs/E2E-TEST-PLAN.md`](docs/E2E-TEST-PLAN.md) and the version-specific pilot notes.

## Building locally

Prerequisites: Windows 11 x64 and .NET 8 SDK.

```powershell
dotnet restore src/G.PcHealthCheck/G.PcHealthCheck.csproj
dotnet build src/G.PcHealthCheck/G.PcHealthCheck.csproj -c Release -warnaserror
dotnet run --project src/G.PcHealthCheck/G.PcHealthCheck.csproj -c Release -- --selftest
dotnet publish src/G.PcHealthCheck/G.PcHealthCheck.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish
```

## Contributing, licensing and signing

See [`CONTRIBUTING.md`](CONTRIBUTING.md). Use synthetic/redacted public reports and the private reporting process in [`SECURITY.md`](SECURITY.md). Source is Apache License 2.0; see [`LICENSE`](LICENSE). G branding is reserved and not licensed under Apache; see [`NOTICE`](NOTICE).

The EXE is not Authenticode-signed. Verify the release checksum/provenance and use an approved distribution channel. Managed installation remains optional; build attestations do not replace a Windows publisher signature.
